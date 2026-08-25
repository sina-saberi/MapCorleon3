using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MapCorleon.Tests;

public sealed class SkyrimPlayerLocationProviderTests
{
    [Theory]
    [InlineData("Skyrim", "Skyrim", true)]
    [InlineData("Skyrim Special Edition", "Skyrim", true)]
    [InlineData("Skyrim", "Solstheim", false)]
    [InlineData("Fallout 4", "Commonwealth", false)]
    public void MatchesOnlySupportedSkyrimMap(string gameName, string mapName, bool expected)
    {
        var provider = CreateProvider(new FakeDetector(), new FakeMemoryReader());
        Assert.Equal(expected, provider.Supports(CreateMap(gameName, mapName)));
    }

    [Fact]
    public async Task ReadsCoordinatesUsingProfileAddresses()
    {
        var profile = SkyrimMemoryProfile.AnniversaryEdition1170;
        var moduleBase = nint.CreateChecked(0x140000000L);
        var addresses = profile.GetPositionAddresses(moduleBase);
        var detector = new FakeDetector
        {
            Process = new GameProcessInfo(42, moduleBase, @"C:\Games\SkyrimSE.exe", "1.6.1170.0")
        };
        var memory = new FakeMemoryReader(new Dictionary<nint, float>
        {
            [addresses.X] = 0,
            [addresses.Y] = 0,
            [addresses.Z] = 1200
        });
        var provider = CreateProvider(detector, memory);

        var result = await provider.ReadAsync(CreateMap("Skyrim", "Skyrim"), CancellationToken.None);

        Assert.Equal(PlayerLocationReadStatus.Success, result.Status);
        Assert.NotNull(result.Location);
        Assert.Equal(42, memory.OpenedProcessId);
        Assert.Equal([addresses.X, addresses.Y, addresses.Z], memory.ReadAddresses);
    }

    [Fact]
    public async Task DetachesWhenProcessExitsAndReopensAfterRestart()
    {
        var firstBase = nint.CreateChecked(0x140000000L);
        var secondBase = nint.CreateChecked(0x150000000L);
        var profile = SkyrimMemoryProfile.AnniversaryEdition1170;
        var first = profile.GetPositionAddresses(firstBase);
        var second = profile.GetPositionAddresses(secondBase);
        var detector = new FakeDetector
        {
            Process = new GameProcessInfo(42, firstBase, @"C:\Games\SkyrimSE.exe", "1.6.1170.0")
        };
        var memory = new FakeMemoryReader(new Dictionary<nint, float>
        {
            [first.X] = 0, [first.Y] = 0, [first.Z] = 100,
            [second.X] = 10, [second.Y] = 20, [second.Z] = 200
        });
        var provider = CreateProvider(detector, memory);

        Assert.Equal(PlayerLocationReadStatus.Success, (await provider.ReadAsync(CreateMap("Skyrim", "Skyrim"), default)).Status);
        detector.Process = null;
        Assert.Equal(PlayerLocationReadStatus.GameNotRunning, (await provider.ReadAsync(CreateMap("Skyrim", "Skyrim"), default)).Status);
        detector.Process = new GameProcessInfo(42, secondBase, @"C:\Games\SkyrimSE.exe", "1.6.1170.0");
        Assert.Equal(PlayerLocationReadStatus.Success, (await provider.ReadAsync(CreateMap("Skyrim", "Skyrim"), default)).Status);

        Assert.Equal([42, 42], memory.OpenedProcessIds);
        Assert.Equal(1, memory.DisposedSessionCount);
    }

    private static SkyrimPlayerLocationProvider CreateProvider(IGameProcessDetector detector, IProcessMemoryReader memory) =>
        new(detector, memory, NullLogger<SkyrimPlayerLocationProvider>.Instance);

    internal static Map CreateMap(string gameName, string mapName) => new()
    {
        Id = 1,
        Name = mapName,
        Game = new Game { Id = 1, Name = gameName },
        Config = new MapConfig
        {
            MinLatitude = .95,
            MaxLatitude = 1.35,
            MinLongitude = -1.35,
            MaxLongitude = -.78
        }
    };

    internal sealed class FakeDetector : IGameProcessDetector
    {
        public GameProcessInfo? Process { get; set; }
        public GameProcessInfo? Find(string processName) => Process;
        public bool IsRunning(string processName) => Process is not null;
    }

    internal sealed class FakeMemoryReader(Dictionary<nint, float>? values = null) : IProcessMemoryReader
    {
        private readonly Dictionary<nint, float> values = values ?? [];
        public int? OpenedProcessId => OpenedProcessIds.LastOrDefault();
        public List<int> OpenedProcessIds { get; } = [];
        public List<nint> ReadAddresses { get; } = [];
        public int DisposedSessionCount { get; private set; }

        public IProcessMemorySession Open(int processId)
        {
            OpenedProcessIds.Add(processId);
            return new Session(values, ReadAddresses, () => DisposedSessionCount++);
        }

        private sealed class Session(Dictionary<nint, float> values, List<nint> reads, Action disposed) : IProcessMemorySession
        {
            public float ReadSingle(nint address)
            {
                reads.Add(address);
                return values[address];
            }
            public void Dispose() => disposed();
        }
    }
}
