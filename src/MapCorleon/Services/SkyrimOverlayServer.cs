using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Threading;
using MapCorleon.Games.Skyrim;
using MapCorleon.Models;
using MapCorleon.ViewModels;
using Microsoft.Extensions.Logging;

namespace MapCorleon.Services;

internal sealed class SkyrimOverlayServer(
    Dispatcher dispatcher,
    ShellViewModel shell,
    Func<bool> toggleFollow,
    ISettingsService settings,
    ILogger logger)
{
    private const string PipeName = "MapCorleon.SkyrimOverlay.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly OverlayNearbyLocations nearby = new();
    private (int MapId, int TimelineId)? currentScope;
    private Guid scopeToken;

    public async Task RunAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await ExchangeAsync(pipe, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (OperationCanceledException) { }
            catch (JsonException) { }
            catch (IOException)
            {
                // The game may close the pipe before reading the response, or another app instance may own it.
                try { await Task.Delay(500, stopping).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skyrim overlay request failed.");
            }
        }
    }

    private async Task ExchangeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var buffer = new byte[1024];
        var received = 0;
        do
        {
            var read = await pipe.ReadAsync(buffer.AsMemory(received), ct).ConfigureAwait(false);
            if (read == 0) return;
            received += read;
        } while (!pipe.IsMessageComplete && received < buffer.Length);
        if (!pipe.IsMessageComplete) return;

        var request = JsonSerializer.Deserialize<OverlayRequest>(buffer.AsSpan(0, received), Json);
        if (request is null) return;
        var response = await dispatcher.InvokeAsync(() => HandleAsync(request, ct), DispatcherPriority.Background, ct)
            .Task.Unwrap().ConfigureAwait(false);
        await pipe.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(response, Json), ct).ConfigureAwait(false);
    }

    private async Task<OverlayState> HandleAsync(OverlayRequest request, CancellationToken ct)
    {
        var scope = shell.CurrentMap is { } map && SkyrimGameIdentity.IsTrackingMap(GameMapDescriptorFactory.Create(map))
            ? shell.LoadedLocationScope
            : null;
        if (currentScope != scope)
        {
            currentScope = scope;
            scopeToken = Guid.NewGuid();
            nearby.Reset();
        }

        var player = scope.HasValue ? shell.LivePlayerLocation : null;
        var preferences = settings.Current.GameOverlays.GetValueOrDefault(SkyrimGameIdentity.OverlaySettingsKey)
            ?? GameOverlaySettings.Default;
        var cards = nearby.Find(player, shell.LoadedLocations, preferences);
        string? feedback = null;
        if (scope is { } activeScope && player is not null && request.Scope == scopeToken)
        {
            if (request.Action == "toggleFollow")
                feedback = toggleFollow() ? "Follow on" : "Follow off";
            else if (cards.Any(card => card.Id == request.LocationId))
            {
                if (request.Action == "dismiss")
                    nearby.Dismiss(request.LocationId);
                else if (request.Action == "collect")
                    feedback = await shell.CollectOverlayLocationAsync(request.LocationId, activeScope, ct)
                        ? "Collected" : "Could not collect. Try again.";
                cards = nearby.Find(shell.LoadedLocationScope == scope ? shell.LivePlayerLocation : null, shell.LoadedLocations, preferences);
            }
        }
        var overlayPreferences = new OverlayPreferences(preferences.PanelScanCode, preferences.FollowScanCode,
            Math.Clamp(preferences.NearbyNotificationSeconds, GameOverlaySettings.MinNotificationSeconds,
                GameOverlaySettings.MaxNotificationSeconds));
        return new OverlayState(scopeToken, player is not null, cards, feedback, overlayPreferences);
    }

    private sealed record OverlayRequest(string Action, Guid Scope = default, int LocationId = 0);
    private sealed record OverlayPreferences(int? PanelScanCode, int? FollowScanCode, int NearbyNotificationSeconds);
    private sealed record OverlayState(Guid Scope, bool CanFollow, IReadOnlyList<OverlayNearbyLocations.Card> Cards,
        string? Feedback, OverlayPreferences Preferences);
}
