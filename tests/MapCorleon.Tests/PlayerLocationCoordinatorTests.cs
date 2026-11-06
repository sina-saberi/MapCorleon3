using MapCorleon.Contracts;
using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MapCorleon.Tests;

public sealed class PlayerLocationCoordinatorTests
{
    [Theory]
    [InlineData(PlayerLocationReadStatus.Interior, PlayerTrackingState.PlayerIsInterior, "Player is indoors")]
    [InlineData(PlayerLocationReadStatus.OutsideSupportedWorldspace, PlayerTrackingState.OutsideSupportedWorldspace, "Player is outside the selected map")]
    public async Task ClearsLocationInUnsupportedAreasAndRestoresItInTheSupportedMap(
        PlayerLocationReadStatus readStatus, PlayerTrackingState state, string statusText)
    {
        var exterior = new PlayerLocation(1.1, -1.0, 100, 200, 300);
        var provider = new FakeProvider { Result = PlayerLocationReadResult.Success(exterior) };
        var coordinator = new PlayerLocationCoordinator(
            [provider],
            NullLogger<PlayerLocationCoordinator>.Instance,
            TimeSpan.FromMilliseconds(10));
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.SetActiveMapAsync(SkyrimPlayerLocationProviderTests.CreateMap("Skyrim", "Skyrim"));

        await WaitUntilAsync(() => coordinator.State == PlayerTrackingState.TrackingPlayer && coordinator.CurrentLocation == exterior);

        provider.Result = new PlayerLocationReadResult(readStatus);
        await WaitUntilAsync(() => coordinator.State == state && coordinator.CurrentLocation is null);
        Assert.Equal(statusText, coordinator.StatusText);

        provider.Result = PlayerLocationReadResult.Success(exterior);
        await WaitUntilAsync(() => coordinator.State == PlayerTrackingState.TrackingPlayer && coordinator.CurrentLocation == exterior);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PollsOnlyMatchingMapAndStopsAfterMapChange()
    {
        var provider = new FakeProvider();
        var coordinator = new PlayerLocationCoordinator(
            [provider],
            NullLogger<PlayerLocationCoordinator>.Instance,
            TimeSpan.FromMilliseconds(10));
        await coordinator.StartAsync(CancellationToken.None);

        await coordinator.SetActiveMapAsync(SkyrimPlayerLocationProviderTests.CreateMap("Fallout 4", "Commonwealth"));
        await Task.Delay(40);
        Assert.Equal(0, provider.ReadCount);

        await coordinator.SetActiveMapAsync(SkyrimPlayerLocationProviderTests.CreateMap("Skyrim", "Skyrim"));
        await WaitUntilAsync(() => provider.ReadCount >= 2);
        await coordinator.SetActiveMapAsync(SkyrimPlayerLocationProviderTests.CreateMap("Fallout 4", "Commonwealth"));
        var stoppedAt = provider.ReadCount;
        await Task.Delay(40);

        Assert.Equal(stoppedAt, provider.ReadCount);
        Assert.True(provider.DetachCount > 0);
        await coordinator.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeProvider : IPlayerLocationProvider
    {
        public int ReadCount { get; private set; }
        public int DetachCount { get; private set; }
        public string Key => "fake";
        public PlayerLocationProviderMetadata Metadata { get; } = new("FAKE", "Fake provider");
        public PlayerLocationReadResult Result { get; set; } = new(PlayerLocationReadStatus.GameNotRunning);
        public bool Supports(GameMapDescriptor map) => map.GameName == "Skyrim" && map.MapName == "Skyrim";
        public Task<PlayerLocationReadResult> ReadAsync(GameMapDescriptor map, CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult(Result);
        }
        public void Detach() => DetachCount++;
    }
}
