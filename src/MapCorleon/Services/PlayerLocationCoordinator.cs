using System.ComponentModel;
using System.Runtime.CompilerServices;
using MapCorleon.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MapCorleon.Services;

public sealed class PlayerLocationCoordinator : IPlayerLocationCoordinator, IHostedService
{
    private readonly IReadOnlyList<IPlayerLocationProvider> providers;
    private readonly ILogger<PlayerLocationCoordinator> logger;
    private readonly TimeSpan pollInterval;
    private readonly SemaphoreSlim transitionGate = new(1, 1);
    private CancellationToken applicationStopping;
    private CancellationTokenSource? activeMapCancellation;
    private Task? activePollingTask;
    private IPlayerLocationProvider? activeProvider;
    private PlayerLocation? currentLocation;
    private PlayerTrackingState state = PlayerTrackingState.Inactive;
    private string statusText = "Player tracking inactive";

    public PlayerLocationCoordinator(
        IEnumerable<IPlayerLocationProvider> providers,
        ILogger<PlayerLocationCoordinator> logger)
        : this(providers, logger, TimeSpan.FromSeconds(1)) { }

    public PlayerLocationCoordinator(
        IEnumerable<IPlayerLocationProvider> providers,
        ILogger<PlayerLocationCoordinator> logger,
        TimeSpan pollInterval)
    {
        this.providers = providers.ToList();
        this.logger = logger;
        this.pollInterval = pollInterval;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerLocation? CurrentLocation
    {
        get => currentLocation;
        private set { if (Equals(currentLocation, value)) return; currentLocation = value; OnPropertyChanged(); }
    }

    public PlayerTrackingState State
    {
        get => state;
        private set { if (state == value) return; state = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => statusText;
        private set { if (statusText == value) return; statusText = value; OnPropertyChanged(); }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        applicationStopping = cancellationToken;
        return Task.CompletedTask;
    }

    public bool Supports(Map map) => providers.Any(provider => provider.Supports(map));

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await transitionGate.WaitAsync(cancellationToken);
        try { await StopPollingAsync(); }
        finally { transitionGate.Release(); }
    }

    public async Task SetActiveMapAsync(Map? map, CancellationToken ct = default)
    {
        await transitionGate.WaitAsync(ct);
        try
        {
            await StopPollingAsync();
            activeProvider = map is null ? null : providers.FirstOrDefault(provider => provider.Supports(map));
            if (activeProvider is null || map is null)
            {
                SetResult(PlayerTrackingState.Inactive, "Player tracking inactive", null);
                return;
            }

            activeMapCancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping, ct);
            activePollingTask = PollAsync(activeProvider, map, activeMapCancellation.Token);
        }
        finally { transitionGate.Release(); }
    }

    public void NotifyGameStarting() => SetResult(PlayerTrackingState.StartingGame, "Starting game", null);

    private async Task PollAsync(IPlayerLocationProvider provider, Map map, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await provider.ReadAsync(map, ct);
                Apply(result);
                await Task.Delay(pollInterval, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Player location polling stopped unexpectedly.");
            SetResult(PlayerTrackingState.UnableToReadLocation, "Unable to read location", null);
        }
        finally { provider.Detach(); }
    }

    private void Apply(PlayerLocationReadResult result)
    {
        var (newState, message) = result.Status switch
        {
            PlayerLocationReadStatus.Success => (PlayerTrackingState.TrackingPlayer, "Tracking player"),
            PlayerLocationReadStatus.GameNotRunning => (PlayerTrackingState.GameNotRunning, "Game not running"),
            PlayerLocationReadStatus.UnsupportedGameVersion => (PlayerTrackingState.UnsupportedGameVersion, "Unsupported game version"),
            _ => (PlayerTrackingState.UnableToReadLocation, "Unable to read location")
        };
        if (State != newState && newState is PlayerTrackingState.UnsupportedGameVersion or PlayerTrackingState.UnableToReadLocation)
            logger.LogWarning("Player tracking state changed to {State}: {Diagnostic}", newState, result.Diagnostic);
        SetResult(newState, message, result.Location);
    }

    private void SetResult(PlayerTrackingState newState, string message, PlayerLocation? location)
    {
        State = newState;
        StatusText = message;
        CurrentLocation = location;
    }

    private async Task StopPollingAsync()
    {
        if (activeMapCancellation is not null)
        {
            await activeMapCancellation.CancelAsync();
            if (activePollingTask is not null)
            {
                try { await activePollingTask; }
                catch (OperationCanceledException) { }
            }
            activeMapCancellation.Dispose();
        }
        activeProvider?.Detach();
        activeMapCancellation = null;
        activePollingTask = null;
        activeProvider = null;
        CurrentLocation = null;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
