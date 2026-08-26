using MapCorleon.Contracts;
using MapCorleon.Games.Skyrim;
using MapCorleon.Models;
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
        Assert.Equal(expected, provider.Supports(CreateDescriptor(gameName, mapName)));
    }

    [Fact]
    public async Task ReadsCoordinatesUsingProfileAddresses()
    {
        var profile = SkyrimWorldMapMemoryProfile.AnniversaryEdition1170;
        var moduleBase = nint.CreateChecked(0x140000000L);
        var addresses = profile.GetPositionAddresses(moduleBase);
        var player = nint.CreateChecked(0x200000000L);
        var cell = nint.CreateChecked(0x210000000L);
        var detector = new FakeDetector
        {
            Process = new GameProcessInfo(42, moduleBase, @"C:\Games\SkyrimSE.exe", "1.6.1170.0")
        };
        var memory = new FakeMemoryReader(new Dictionary<nint, float>
        {
            [addresses.X] = 0,
            [addresses.Y] = 0,
            [addresses.Z] = 1200
        }, new Dictionary<nint, nint>
        {
            [profile.GetPlayerCharacterSingletonAddress(moduleBase)] = player,
            [player + profile.Layout.PlayerParentCellOffset] = cell
        }, new Dictionary<nint, ushort>
        {
            [cell + profile.Layout.CellFlagsOffset] = 0
        }, new Dictionary<nint, uint>
        {
            [cell + profile.Layout.CellFormIdOffset] = 0x0000003C
        });
        var provider = CreateProvider(detector, memory);

        var result = await provider.ReadAsync(CreateDescriptor("Skyrim", "Skyrim"), CancellationToken.None);

        Assert.Equal(PlayerLocationReadStatus.Success, result.Status);
        Assert.NotNull(result.Location);
        Assert.Equal(42, memory.OpenedProcessId);
        Assert.Equal([addresses.X, addresses.Y, addresses.Z], memory.ReadAddresses);
    }

    [Fact]
    public async Task ReturnsInteriorWithoutReadingExteriorCoordinates()
    {
        var profile = SkyrimWorldMapMemoryProfile.AnniversaryEdition1170;
        var moduleBase = nint.CreateChecked(0x140000000L);
        var player = nint.CreateChecked(0x200000000L);
        var cell = nint.CreateChecked(0x210000000L);
        var detector = new FakeDetector
        {
            Process = new GameProcessInfo(42, moduleBase, @"C:\Games\SkyrimSE.exe", "1.6.1170.0")
        };
        var memory = new FakeMemoryReader(
            pointers: new Dictionary<nint, nint>
            {
                [profile.GetPlayerCharacterSingletonAddress(moduleBase)] = player,
                [player + profile.Layout.PlayerParentCellOffset] = cell
            },
            uint16Values: new Dictionary<nint, ushort>
            {
                [cell + profile.Layout.CellFlagsOffset] = 0x1
            },
            uint32Values: new Dictionary<nint, uint>
            {
                [cell + profile.Layout.CellFormIdOffset] = 0x00018A53
            });
        var provider = CreateProvider(detector, memory);

        var result = await provider.ReadAsync(CreateDescriptor("Skyrim", "Skyrim"), default);

        Assert.Equal(PlayerLocationReadStatus.Interior, result.Status);
        Assert.Null(result.Location);
        Assert.Empty(memory.ReadAddresses);
    }

    [Fact]
    public async Task HidesLocationWhilePlayerPointerIsUnavailable()
    {
        var profile = SkyrimWorldMapMemoryProfile.AnniversaryEdition1170;
        var moduleBase = nint.CreateChecked(0x140000000L);
        var detector = new FakeDetector
        {
            Process = new GameProcessInfo(42, moduleBase, @"C:\Games\SkyrimSE.exe", "1.6.1170.0")
        };
        var memory = new FakeMemoryReader(pointers: new Dictionary<nint, nint>
        {
            [profile.GetPlayerCharacterSingletonAddress(moduleBase)] = 0
        });
        var provider = CreateProvider(detector, memory);

        var result = await provider.ReadAsync(CreateDescriptor("Skyrim", "Skyrim"), default);

        Assert.Equal(PlayerLocationReadStatus.UnableToRead, result.Status);
        Assert.Null(result.Location);
        Assert.Empty(memory.ReadAddresses);
    }

    [Fact]
    public async Task DetachesWhenProcessExitsAndReopensAfterRestart()
    {
        var firstBase = nint.CreateChecked(0x140000000L);
        var secondBase = nint.CreateChecked(0x150000000L);
        var profile = SkyrimWorldMapMemoryProfile.AnniversaryEdition1170;
        var first = profile.GetPositionAddresses(firstBase);
        var second = profile.GetPositionAddresses(secondBase);
        var firstPlayer = nint.CreateChecked(0x200000000L);
        var firstCell = nint.CreateChecked(0x210000000L);
        var secondPlayer = nint.CreateChecked(0x220000000L);
        var secondCell = nint.CreateChecked(0x230000000L);
        var detector = new FakeDetector
        {
            Process = new GameProcessInfo(42, firstBase, @"C:\Games\SkyrimSE.exe", "1.6.1170.0")
        };
        var memory = new FakeMemoryReader(new Dictionary<nint, float>
        {
            [first.X] = 0, [first.Y] = 0, [first.Z] = 100,
            [second.X] = 10, [second.Y] = 20, [second.Z] = 200
        }, new Dictionary<nint, nint>
        {
            [profile.GetPlayerCharacterSingletonAddress(firstBase)] = firstPlayer,
            [firstPlayer + profile.Layout.PlayerParentCellOffset] = firstCell,
            [profile.GetPlayerCharacterSingletonAddress(secondBase)] = secondPlayer,
            [secondPlayer + profile.Layout.PlayerParentCellOffset] = secondCell
        }, new Dictionary<nint, ushort>
        {
            [firstCell + profile.Layout.CellFlagsOffset] = 0,
            [secondCell + profile.Layout.CellFlagsOffset] = 0
        }, new Dictionary<nint, uint>
        {
            [firstCell + profile.Layout.CellFormIdOffset] = 0x3C,
            [secondCell + profile.Layout.CellFormIdOffset] = 0x3C
        });
        var provider = CreateProvider(detector, memory);

        Assert.Equal(PlayerLocationReadStatus.Success, (await provider.ReadAsync(CreateDescriptor("Skyrim", "Skyrim"), default)).Status);
        detector.Process = null;
        Assert.Equal(PlayerLocationReadStatus.GameNotRunning, (await provider.ReadAsync(CreateDescriptor("Skyrim", "Skyrim"), default)).Status);
        detector.Process = new GameProcessInfo(42, secondBase, @"C:\Games\SkyrimSE.exe", "1.6.1170.0");
        Assert.Equal(PlayerLocationReadStatus.Success, (await provider.ReadAsync(CreateDescriptor("Skyrim", "Skyrim"), default)).Status);

        Assert.Equal([42, 42], memory.OpenedProcessIds);
        Assert.Equal(1, memory.DisposedSessionCount);
    }

    private static SkyrimWorldMapPlayerLocationProvider CreateProvider(IGameProcessDetector detector, IProcessMemoryReader memory) =>
        new(detector, memory, NullLogger<SkyrimWorldMapPlayerLocationProvider>.Instance);

    internal static GameMapDescriptor CreateDescriptor(string gameName, string mapName) =>
        new(1, gameName, mapName, .95, 1.35, -1.35, -.78);

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

    internal sealed class FakeMemoryReader(
        Dictionary<nint, float>? singleValues = null,
        Dictionary<nint, nint>? pointers = null,
        Dictionary<nint, ushort>? uint16Values = null,
        Dictionary<nint, uint>? uint32Values = null) : IProcessMemoryReader
    {
        private readonly Dictionary<nint, float> singleValues = singleValues ?? [];
        private readonly Dictionary<nint, nint> pointers = pointers ?? [];
        private readonly Dictionary<nint, ushort> uint16Values = uint16Values ?? [];
        private readonly Dictionary<nint, uint> uint32Values = uint32Values ?? [];
        public int? OpenedProcessId => OpenedProcessIds.LastOrDefault();
        public List<int> OpenedProcessIds { get; } = [];
        public List<nint> ReadAddresses { get; } = [];
        public int DisposedSessionCount { get; private set; }

        public IProcessMemorySession Open(int processId)
        {
            OpenedProcessIds.Add(processId);
            return new Session(singleValues, pointers, uint16Values, uint32Values, ReadAddresses, () => DisposedSessionCount++);
        }

        private sealed class Session(
            Dictionary<nint, float> singleValues,
            Dictionary<nint, nint> pointers,
            Dictionary<nint, ushort> uint16Values,
            Dictionary<nint, uint> uint32Values,
            List<nint> singleReads,
            Action disposed) : IProcessMemorySession
        {
            public float ReadSingle(nint address)
            {
                singleReads.Add(address);
                return singleValues[address];
            }
            public ushort ReadUInt16(nint address) => uint16Values[address];
            public uint ReadUInt32(nint address) => uint32Values[address];
            public nint ReadPointer(nint address) => pointers[address];
            public void Dispose() => disposed();
        }
    }
}
