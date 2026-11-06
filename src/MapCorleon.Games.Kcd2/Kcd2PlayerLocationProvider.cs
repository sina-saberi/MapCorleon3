using System.ComponentModel;
using MapCorleon.Contracts;

namespace MapCorleon.Games.Kcd2;

public sealed class Kcd2PlayerLocationProvider(
    IGameProcessDetector processDetector, IProcessMemoryReader memoryReader) : IPlayerLocationProvider
{
    private const string EngineModuleName = "WHGame.dll";
    // Steam build 23914554. See docs/kcd2-tracking.md for the coordinate getter and layout.
    private const uint EngineTimestamp = 0x6A350E20;
    private const uint EngineImageSize = 0x05B2D000;
    private const int ObserverManagerOffset = 0x05496F10;
    private const int PrimaryObserverOffset = 0x20;
    private const int ObserverPositionOffset = 0x18;
    private const int ObserverVTableOffset = 0x03ADB510;

    private GameProcessInfo? attached;
    private IProcessMemorySession? session;
    private Kcd2LevelLog? levelLog;
    private nint engine;

    public string Key => "kcd2";
    public PlayerLocationProviderMetadata Metadata { get; } = new(
        "KCD2 PLAYER TRACKING", "Tracking reads the running Kingdom Come: Deliverance II process.");
    public bool Supports(GameMapDescriptor map) => Kcd2MapProfile.ForMap(map) is not null;

    public Task<PlayerLocationReadResult> ReadAsync(GameMapDescriptor map, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Read(map));
    }

    private PlayerLocationReadResult Read(GameMapDescriptor map)
    {
        if (Kcd2MapProfile.ForMap(map) is not { } profile) return Unavailable("The selected KCD2 map is unsupported.");

        var process = processDetector.Find(Kcd2MapProfile.ProcessName);
        if (process is null)
        {
            Detach();
            return new(PlayerLocationReadStatus.GameNotRunning);
        }

        if (process.FileVersion != Kcd2MapProfile.ExecutableVersion)
        {
            Detach();
            return Unsupported($"KingdomCome.exe {process.FileVersion} has no KCD2 memory profile.");
        }

        try
        {
            if (attached != process)
            {
                Detach();
                engine = processDetector.FindModuleBaseAddress(process.Id, EngineModuleName);
                if (engine == 0) return Unavailable("The KCD2 engine is still loading.");

                session = memoryReader.Open(process.Id);
                if (!MatchesEngine(session, engine))
                {
                    Detach();
                    return Unsupported("WHGame.dll does not match the KCD2 Steam 1.5.6 memory profile.");
                }

                var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(process.ExecutablePath)!, "..", ".."));
                levelLog = new Kcd2LevelLog(Path.Combine(root, "kcd.log"));
                attached = process;
            }

            var level = levelLog!.ReadLevel();
            if (level is null) return Unavailable("No KCD2 level is loaded.");
            if (!profile.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                return new(PlayerLocationReadStatus.OutsideSupportedWorldspace, Diagnostic: $"KCD2 level: {level}.");

            var memory = session!;
            var manager = memory.ReadPointer(engine + ObserverManagerOffset);
            if (manager == 0) return Unavailable("The player observer is still loading.");
            var observer = memory.ReadPointer(manager + PrimaryObserverOffset);
            if (observer == 0 || memory.ReadPointer(observer) != engine + ObserverVTableOffset)
                return Unavailable("The primary player observer is unavailable.");

            var address = observer + ObserverPositionOffset;
            var position = new GamePosition(memory.ReadSingle(address),
                memory.ReadSingle(address + sizeof(float)), memory.ReadSingle(address + 2 * sizeof(float)));
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
                position.X is <= 0 or > 4096 || position.Y is <= 0 or > 4096 || Math.Abs(position.Z) > 2000)
                return Unavailable("KCD2 returned no valid world position.");
            if (memory.ReadPointer(manager + PrimaryObserverOffset) != observer || levelLog.ReadLevel() != level)
                return Unavailable("The KCD2 player or level changed during the read.");

            return PlayerLocationReadResult.Success(profile.Project(position));
        }
        catch (IOException ex)
        {
            // The log can be replaced or temporarily locked as the engine starts.
            return Unavailable(ex.Message);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            Detach();
            return Unavailable(ex.Message);
        }
    }

    private static bool MatchesEngine(IProcessMemorySession memory, nint address)
    {
        if (memory.ReadUInt16(address) != 0x5A4D) return false;
        var offset = memory.ReadUInt32(address + 0x3C);
        if (offset is < 0x40 or > 0x1000) return false;
        var header = address + (int)offset;
        return memory.ReadUInt32(header) == 0x4550 &&
            memory.ReadUInt32(header + 8) == EngineTimestamp &&
            memory.ReadUInt32(header + 0x50) == EngineImageSize;
    }

    private static PlayerLocationReadResult Unavailable(string diagnostic) =>
        new(PlayerLocationReadStatus.UnableToRead, Diagnostic: diagnostic);

    private static PlayerLocationReadResult Unsupported(string diagnostic) =>
        new(PlayerLocationReadStatus.UnsupportedGameVersion, Diagnostic: diagnostic);

    public void Detach()
    {
        session?.Dispose();
        session = null;
        levelLog = null;
        attached = null;
        engine = 0;
    }
}
