using MapCorleon.Contracts;
using MapCorleon.Games.Fallout4;
using static MapCorleon.Tests.SkyrimPlayerLocationProviderTests;

namespace MapCorleon.Tests;

public sealed class Fallout4PlayerLocationProviderTests
{
    private static readonly GameMapDescriptor Commonwealth = new(200, "Fallout 4", "Commonwealth");
    private static readonly nint Module = nint.CreateChecked(0x140000000L);
    private static readonly nint Player = nint.CreateChecked(0x200000000L);
    private static readonly nint Cell = nint.CreateChecked(0x210000000L);
    private static readonly nint Worldspace = nint.CreateChecked(0x220000000L);

    [Theory]
    [InlineData("Fallout 4", "Commonwealth", true)]
    [InlineData("Fallout 4", "Far Harbor", false)]
    [InlineData("Fallout 4", "Nuka-World", false)]
    [InlineData("Skyrim", "Commonwealth", false)]
    public void SupportsOnlyCommonwealth(string game, string map, bool supported)
    {
        var provider = new Fallout4PlayerLocationProvider(new FakeDetector(), new FakeMemoryReader());
        Assert.Equal(supported, provider.Supports(new GameMapDescriptor(1, game, map)));
    }

    [Fact]
    public async Task ProjectsKnownCommonwealthLandmarkFromPlayerCoordinates()
    {
        var detector = CreateDetector("1.11.221.0");
        var memory = CreateMemory();
        var provider = new Fallout4PlayerLocationProvider(detector, memory);

        var result = await provider.ReadAsync(Commonwealth, default);

        Assert.Equal(PlayerLocationReadStatus.Success, result.Status);
        var location = Assert.IsType<PlayerLocation>(result.Location);
        Assert.Equal(69_518.8828125f, location.GameX);
        Assert.Equal(89_355.265625f, location.GameY);
        Assert.Equal(1_742.61767578125f, location.GameZ);
        Assert.InRange(location.Latitude, 0.9985, 0.9996);
        Assert.InRange(location.Longitude, -0.4921, -0.4910);
        Assert.Equal([Player + 0xD0, Player + 0xD4, Player + 0xD8], memory.ReadAddresses);
    }

    [Theory]
    [InlineData(1, 0x3C, PlayerLocationReadStatus.Interior)]
    [InlineData(0, 0x03000B00, PlayerLocationReadStatus.OutsideSupportedWorldspace)]
    public async Task RejectsOtherWorldspacesAndInteriorsBeforeReadingPosition(
        ushort flags, uint worldspace, PlayerLocationReadStatus status)
    {
        var memory = CreateMemory(flags, worldspace);
        var provider = new Fallout4PlayerLocationProvider(CreateDetector("1.11.221"), memory);

        var result = await provider.ReadAsync(Commonwealth, default);

        Assert.Equal(status, result.Status);
        Assert.Null(result.Location);
        Assert.Empty(memory.ReadAddresses);
    }

    [Theory]
    [InlineData("1.11.240.0")]
    [InlineData("1.11.221.1")]
    [InlineData("")]
    public async Task RejectsUnprofiledRuntimeBeforeOpeningMemory(string version)
    {
        var memory = CreateMemory();
        var provider = new Fallout4PlayerLocationProvider(CreateDetector(version), memory);

        var result = await provider.ReadAsync(Commonwealth, default);

        Assert.Equal(PlayerLocationReadStatus.UnsupportedGameVersion, result.Status);
        Assert.Empty(memory.OpenedProcessIds);
    }

    [Fact]
    public async Task RejectsAReferenceThatIsNotThePlayer()
    {
        var memory = CreateMemory(playerFormId: 0x15);
        var provider = new Fallout4PlayerLocationProvider(CreateDetector("1.11.221.0"), memory);

        Assert.Equal(PlayerLocationReadStatus.UnableToRead, (await provider.ReadAsync(Commonwealth, default)).Status);
        Assert.Empty(memory.ReadAddresses);
    }

    [Fact]
    public async Task ClearsLocationWhenNoGameIsRunningAndReattachesOnRestart()
    {
        var detector = CreateDetector("1.11.221.0");
        var memory = CreateMemory();
        var provider = new Fallout4PlayerLocationProvider(detector, memory);
        var original = detector.Process!;

        Assert.Equal(PlayerLocationReadStatus.Success, (await provider.ReadAsync(Commonwealth, default)).Status);
        detector.Process = null;
        Assert.Equal(PlayerLocationReadStatus.GameNotRunning, (await provider.ReadAsync(Commonwealth, default)).Status);
        detector.Process = original with { Id = 43 };
        Assert.Equal(PlayerLocationReadStatus.Success, (await provider.ReadAsync(Commonwealth, default)).Status);
        Assert.Equal([42, 43], memory.OpenedProcessIds);
        Assert.Equal(1, memory.DisposedSessionCount);
    }

    private static FakeDetector CreateDetector(string version) => new()
    {
        Process = new GameProcessInfo(42, Module, @"C:\Fallout 4\Fallout4.exe", version)
    };

    private static FakeMemoryReader CreateMemory(ushort flags = 0, uint worldspaceId = 0x3C, uint playerFormId = 0x14) => new(
        singleValues: new()
        {
            [Player + 0xD0] = 69_518.8828125f,
            [Player + 0xD4] = 89_355.265625f,
            [Player + 0xD8] = 1_742.61767578125f
        },
        pointers: new()
        {
            [Module + 0x032D22E0] = Player,
            [Player + 0xB8] = Cell,
            [Cell + 0xC8] = Worldspace
        },
        uint16Values: new() { [Cell + 0x40] = flags },
        uint32Values: new()
        {
            [Player + 0x14] = playerFormId,
            [Cell + 0x14] = 0x12345,
            [Worldspace + 0x14] = worldspaceId
        });
}
