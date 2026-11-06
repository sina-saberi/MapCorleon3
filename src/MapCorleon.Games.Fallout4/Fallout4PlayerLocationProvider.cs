using System.ComponentModel;
using MapCorleon.Contracts;

namespace MapCorleon.Games.Fallout4;

public sealed class Fallout4PlayerLocationProvider(
    IGameProcessDetector processDetector,
    IProcessMemoryReader memoryReader) : IPlayerLocationProvider
{
    private IProcessMemorySession? session;
    private GameProcessInfo? attachedProcess;

    public string Key => "fallout4";

    public PlayerLocationProviderMetadata Metadata { get; } = new(
        "FALLOUT 4 PLAYER TRACKING",
        "Tracking reads the running Fallout4.exe process for the selected Fallout 4 map.");

    public bool Supports(GameMapDescriptor map) => Fallout4MapProfile.ForMap(map) is not null;

    public Task<PlayerLocationReadResult> ReadAsync(GameMapDescriptor map, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Read(map));
    }

    private PlayerLocationReadResult Read(GameMapDescriptor map)
    {
        if (Fallout4MapProfile.ForMap(map) is not { } mapProfile)
            return Unavailable("The selected Fallout 4 map is not supported.");

        var process = processDetector.Find(Fallout4GameIdentity.ProcessName);
        if (process is null)
        {
            Detach();
            return new PlayerLocationReadResult(PlayerLocationReadStatus.GameNotRunning);
        }

        var profile = Fallout4MemoryProfile.ForVersion(process.FileVersion);
        if (profile is null)
        {
            Detach();
            return new PlayerLocationReadResult(PlayerLocationReadStatus.UnsupportedGameVersion,
                Diagnostic: $"Fallout4.exe {process.FileVersion} has no memory profile.");
        }

        try
        {
            if (attachedProcess != process)
            {
                Detach();
                session = memoryReader.Open(process.Id);
                attachedProcess = process;
            }

            return ReadLocation(session!, process.ModuleBaseAddress, profile, mapProfile);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Detach();
            return Unavailable(ex.Message);
        }
    }

    private static PlayerLocationReadResult ReadLocation(
        IProcessMemorySession memory, nint moduleBase, Fallout4MemoryProfile profile, Fallout4MapProfile mapProfile)
    {
        var singletonAddress = moduleBase + profile.PlayerSingletonOffset;
        var player = memory.ReadPointer(singletonAddress);
        if (!IsUserPointer(player) || memory.ReadUInt32(player + Fallout4MemoryProfile.FormIdOffset) != Fallout4MemoryProfile.PlayerFormId)
            return Unavailable("Fallout 4 player reference is unavailable.");

        var parentCellAddress = player + profile.ParentCellOffset;
        var cell = memory.ReadPointer(parentCellAddress);
        if (!IsUserPointer(cell))
            return Unavailable("Fallout 4 player cell is unavailable.");

        var cellId = memory.ReadUInt32(cell + Fallout4MemoryProfile.FormIdOffset);
        if ((memory.ReadUInt16(cell + profile.CellFlagsOffset) & Fallout4MemoryProfile.InteriorFlag) != 0)
            return new PlayerLocationReadResult(PlayerLocationReadStatus.Interior, Diagnostic: $"Cell 0x{cellId:X8}.");

        var worldspace = memory.ReadPointer(cell + profile.CellWorldspaceOffset);
        if (!IsUserPointer(worldspace))
            return Unavailable($"Cell 0x{cellId:X8} has no readable worldspace.");

        var worldspaceId = memory.ReadUInt32(worldspace + Fallout4MemoryProfile.FormIdOffset);
        if (worldspaceId != mapProfile.WorldspaceId)
            return new PlayerLocationReadResult(PlayerLocationReadStatus.OutsideSupportedWorldspace,
                Diagnostic: $"Worldspace 0x{worldspaceId:X8}, cell 0x{cellId:X8}.");

        var xyz = player + profile.PositionOffset;
        var position = new GamePosition(
            memory.ReadSingle(xyz),
            memory.ReadSingle(xyz + sizeof(float)),
            memory.ReadSingle(xyz + 2 * sizeof(float)));
        if (!IsReasonable(position.X) || !IsReasonable(position.Y) || !IsReasonable(position.Z))
            return Unavailable("Fallout 4 returned invalid player coordinates.");

        if (memory.ReadPointer(singletonAddress) != player || memory.ReadPointer(parentCellAddress) != cell)
            return Unavailable("Fallout 4 player or cell changed during the read.");

        return new PlayerLocationReadResult(PlayerLocationReadStatus.Success, mapProfile.Project(position),
            $"Worldspace 0x{worldspaceId:X8}, cell 0x{cellId:X8}.");
    }

    public void Detach()
    {
        session?.Dispose();
        session = null;
        attachedProcess = null;
    }

    private static PlayerLocationReadResult Unavailable(string diagnostic) =>
        new(PlayerLocationReadStatus.UnableToRead, Diagnostic: diagnostic);

    private static bool IsUserPointer(nint pointer) => pointer.ToInt64() is >= 0x10000 and <= 0x00007FFFFFFFFFFF;

    private static bool IsReasonable(float coordinate) => float.IsFinite(coordinate) && Math.Abs(coordinate) <= 1_000_000;
}
