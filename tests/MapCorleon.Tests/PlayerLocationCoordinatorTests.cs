using MapCorleon.Contracts;
using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MapCorleon.Tests;

public sealed class PlayerLocationCoordinatorTests
{
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
        public bool Supports(GameMapDescriptor map) => map.GameName == "Skyrim" && map.MapName == "Skyrim";
        public Task<PlayerLocationReadResult> ReadAsync(GameMapDescriptor map, CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult(new PlayerLocationReadResult(PlayerLocationReadStatus.GameNotRunning));
        }
        public void Detach() => DetachCount++;
    }
}
