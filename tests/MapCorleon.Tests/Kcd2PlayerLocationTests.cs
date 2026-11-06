using MapCorleon.Contracts;
using MapCorleon.Games.Kcd2;
using MapCorleon.Services;
using static MapCorleon.Tests.SkyrimPlayerLocationProviderTests;

namespace MapCorleon.Tests;

public sealed class Kcd2PlayerLocationTests
{
    private static readonly nint Engine = nint.CreateChecked(0x180000000L);
    private static readonly nint ObserverManager = nint.CreateChecked(0x200000000L);
    private static readonly nint Observer = nint.CreateChecked(0x210000000L);
    private static readonly GameMapDescriptor Trosky = new(738, Kcd2MapProfile.GameName, "Trosky");

    [Theory]
    [InlineData("Trosky", "trosecko", 2506.407f, 2549.512f, -.520949, .830605)]
    [InlineData("Kuttenberg", "kutnohorsko", 698.2417f, 3287.759f, -.925862, .721861)]
    [InlineData("Sedletz Monastery", "klaster", 1304.568f, 1032.831f, .1876, .1674)]
    public void ProjectsAllThreeGameLevels(string map, string level, float x, float y, double longitude, double latitude)
    {
        var profile = Assert.IsType<Kcd2MapProfile>(Kcd2MapProfile.ForMap(Trosky with { MapName = map }));
        Assert.Equal(level, profile.Level);
        var point = profile.Project(new GamePosition(x, y, 30));
        Assert.InRange(point.Longitude, longitude - .002, longitude + .002);
        Assert.InRange(point.Latitude, latitude - .002, latitude + .002);
        Assert.Equal(30, point.GameZ);

        var other = profile.Project(new GamePosition(x + 30, y + 40, 30));
        var distance = Math.Sqrt(profile.GameDistanceSquared(other.Longitude - point.Longitude, other.Latitude - point.Latitude));
        Assert.InRange(distance, 49.99, 50.01);
    }

    [Fact]
    public async Task ReadsLiveCoordinatesOnlyWhenTheLoadedLevelMatches()
    {
        var root = Directory.CreateTempSubdirectory("MapCorleon-Kcd2-");
        var log = Path.Combine(root.FullName, "kcd.log");
        var exe = Path.Combine(root.FullName, "Bin", "Win64", "KingdomCome.exe");
        var memory = CreateMemory();
        var provider = new Kcd2PlayerLocationProvider(new Detector(exe), memory);
        try
        {
            await File.WriteAllTextAsync(log, "================ Loading level trosecko ================\n");
            var result = await provider.ReadAsync(Trosky, default);
            var location = Assert.IsType<PlayerLocation>(result.Location);
            Assert.Equal(PlayerLocationReadStatus.Success, result.Status);
            Assert.Equal(1123.472f, location.GameX);

            await File.AppendAllTextAsync(log, "================ Loading level kutnohorsko ================\n");
            Assert.Equal(PlayerLocationReadStatus.OutsideSupportedWorldspace, (await provider.ReadAsync(Trosky, default)).Status);
            Assert.Equal(PlayerLocationReadStatus.Success,
                (await provider.ReadAsync(Trosky with { MapName = "Kuttenberg" }, default)).Status);

            await File.AppendAllTextAsync(log, "================ Loading level klaster ================\n");
            Assert.Equal(PlayerLocationReadStatus.Success,
                (await provider.ReadAsync(Trosky with { MapName = "Sedletz Monastery" }, default)).Status);
        }
        finally
        {
            provider.Detach();
            File.Delete(log);
            root.Delete();
        }
    }

    [Fact]
    public async Task RejectsAnUnrecognizedEngineBeforeReadingPlayerMemory()
    {
        var memory = CreateMemory(timestamp: 0);
        var provider = new Kcd2PlayerLocationProvider(new Detector(@"C:\KCD2\Bin\Win64\KingdomCome.exe"), memory);
        Assert.Equal(PlayerLocationReadStatus.UnsupportedGameVersion, (await provider.ReadAsync(Trosky, default)).Status);
        Assert.Empty(memory.ReadAddresses);
        Assert.Equal(1, memory.DisposedSessionCount);
    }

    [Fact]
    public void DesktopOverlayUsesSeparateKcd2SettingsAndRejectsTheFirstGame()
    {
        var map = DesktopOverlayProfile.ForMap(Trosky);
        Assert.Equal("kcd2", map?.Game.Key);
        Assert.Equal(75, map?.Game.DefaultRadius);
        Assert.Null(DesktopOverlayProfile.ForMap(Trosky with { GameName = "Kingdom Come: Deliverance" }));
        Assert.Equal("fallout4", DesktopOverlayProfile.ForMap(new(200, "Fallout 4", "Commonwealth"))?.Game.Key);
    }

    private static FakeMemoryReader CreateMemory(uint timestamp = 0x6A350E20) => new(
        singleValues: new()
        {
            [Observer + 0x18] = 1123.472f,
            [Observer + 0x1C] = 1940.152f,
            [Observer + 0x20] = 23.65f
        },
        pointers: new()
        {
            [Engine + 0x05496F10] = ObserverManager,
            [ObserverManager + 0x20] = Observer,
            [Observer] = Engine + 0x03ADB510
        },
        uint16Values: new() { [Engine] = 0x5A4D },
        uint32Values: new()
        {
            [Engine + 0x3C] = 0x100,
            [Engine + 0x100] = 0x4550,
            [Engine + 0x108] = timestamp,
            [Engine + 0x150] = 0x05B2D000
        });

    private sealed class Detector(string executable) : IGameProcessDetector
    {
        public GameProcessInfo? Find(string processName) =>
            processName == Kcd2MapProfile.ProcessName ? new(17, 0, executable, "1.5.6.0") : null;
        public bool IsRunning(string processName) => Find(processName) is not null;
        public nint FindModuleBaseAddress(int processId, string moduleName) =>
            processId == 17 && moduleName == "WHGame.dll" ? Engine : 0;
    }
}
