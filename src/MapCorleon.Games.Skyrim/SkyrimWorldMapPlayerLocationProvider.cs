using MapCorleon.Contracts;
using Microsoft.Extensions.Logging;

namespace MapCorleon.Games.Skyrim;

public sealed class SkyrimWorldMapPlayerLocationProvider(
    IGameProcessDetector processDetector,
    IProcessMemoryReader memoryReader,
    ILogger<SkyrimWorldMapPlayerLocationProvider> logger) : IPlayerLocationProvider
{
    public const string TargetProcessName = "SkyrimSE";
    private static readonly SkyrimWorldMapMemoryProfile[] Profiles =
        [SkyrimWorldMapMemoryProfile.AnniversaryEdition1170];

    private IProcessMemorySession? session;
    private int? attachedProcessId;
    private nint? attachedModuleBase;
    private ProcessMemoryAddresses addresses;
    private nint playerCharacterSingletonAddress;
    private SkyrimWorldMapMemoryProfile? attachedProfile;
    private nint? lastPlayerCharacter;
    private nint? lastCell;
    private bool? lastInteriorState;

    public string Key => "skyrim-se:skyrim-world";
    public PlayerLocationProviderMetadata Metadata { get; } = new(
        "SKYRIM PLAYER TRACKING",
        "Tracking reads the running SkyrimSE.exe process for the selected Skyrim map.");

    public bool Supports(GameMapDescriptor map) => SkyrimGameIdentity.IsTrackingMap(map);

    public Task<PlayerLocationReadResult> ReadAsync(GameMapDescriptor map, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Read(map));
    }

    private PlayerLocationReadResult Read(GameMapDescriptor map)
    {
        if (!Supports(map))
            return new PlayerLocationReadResult(
                PlayerLocationReadStatus.UnableToRead,
                Diagnostic: "The selected map is not supported.");

        var process = processDetector.Find(TargetProcessName);
        if (process is null)
        {
            Detach();
            return new PlayerLocationReadResult(PlayerLocationReadStatus.GameNotRunning);
        }

        try
        {
            if (attachedProcessId != process.Id || attachedModuleBase != process.ModuleBaseAddress)
            {
                Detach();
                var profile = Profiles.FirstOrDefault(candidate => candidate.SupportsVersion(process.FileVersion));
                if (profile is null)
                    return new PlayerLocationReadResult(
                        PlayerLocationReadStatus.UnsupportedGameVersion,
                        Diagnostic: $"SkyrimSE.exe {process.FileVersion} is not supported.");

                session = memoryReader.Open(process.Id);
                attachedProcessId = process.Id;
                attachedModuleBase = process.ModuleBaseAddress;
                attachedProfile = profile;
                addresses = profile.GetPositionAddresses(process.ModuleBaseAddress);
                playerCharacterSingletonAddress = profile.GetPlayerCharacterSingletonAddress(process.ModuleBaseAddress);
                logger.LogDebug(
                    "Skyrim {Version} detected. Module base 0x{ModuleBase:X}; PlayerCharacter singleton 0x{Singleton:X}.",
                    profile.Version,
                    process.ModuleBaseAddress,
                    playerCharacterSingletonAddress);
            }

            var playerCharacter = session!.ReadPointer(playerCharacterSingletonAddress);
            if (!IsLikelyPointer(playerCharacter))
                return new PlayerLocationReadResult(
                    PlayerLocationReadStatus.UnableToRead,
                    Diagnostic: "Skyrim PlayerCharacter is temporarily unavailable.");
            if (lastPlayerCharacter != playerCharacter)
            {
                logger.LogDebug("Resolved Skyrim PlayerCharacter pointer 0x{PlayerCharacter:X}.", playerCharacter);
                lastPlayerCharacter = playerCharacter;
            }

            var parentCell = session.ReadPointer(playerCharacter + attachedProfile!.Layout.PlayerParentCellOffset);
            if (!IsLikelyPointer(parentCell))
                return new PlayerLocationReadResult(
                    PlayerLocationReadStatus.UnableToRead,
                    Diagnostic: "Skyrim parent cell is temporarily unavailable.");

            var cellFormId = session.ReadUInt32(parentCell + attachedProfile.Layout.CellFormIdOffset);
            var cellFlags = session.ReadUInt16(parentCell + attachedProfile.Layout.CellFlagsOffset);
            var isInterior = (cellFlags & 0x1) != 0;
            if (lastCell != parentCell || lastInteriorState != isInterior)
            {
                logger.LogDebug(
                    "Skyrim cell changed: pointer 0x{Cell:X}, FormID 0x{FormId:X8}, Interior {IsInterior}.",
                    parentCell,
                    cellFormId,
                    isInterior);
                lastCell = parentCell;
                lastInteriorState = isInterior;
            }
            if (isInterior)
                return new PlayerLocationReadResult(
                    PlayerLocationReadStatus.Interior,
                    Diagnostic: $"Player is inside cell 0x{cellFormId:X8}.");

            var worldspace = session.ReadPointer(parentCell + attachedProfile.Layout.CellWorldspaceOffset);
            if (!IsLikelyPointer(worldspace))
                return new PlayerLocationReadResult(PlayerLocationReadStatus.UnableToRead,
                    Diagnostic: $"Skyrim cell 0x{cellFormId:X8} has no readable worldspace.");

            var worldspaceId = session.ReadUInt32(worldspace + attachedProfile.Layout.CellFormIdOffset);
            var isSolstheimMap = SkyrimGameIdentity.IsSolstheimMap(map);
            // Tamriel and the five walled cities use the same map coordinates.
            var matchesMap = isSolstheimMap
                ? worldspaceId == 0x04000800
                : worldspaceId is 0x3C or 0x1691D or 0x16BB4 or 0x16D71 or 0x1A26F or 0x37EDF;
            if (!matchesMap)
                return new PlayerLocationReadResult(PlayerLocationReadStatus.OutsideSupportedWorldspace,
                    Diagnostic: $"Skyrim worldspace 0x{worldspaceId:X8}, cell 0x{cellFormId:X8}.");

            // Position remains on the already verified static XYZ source until the
            // PlayerCharacter position layout is live-verified independently.
            var position = new GamePosition(
                session.ReadSingle(addresses.X),
                session.ReadSingle(addresses.Y),
                session.ReadSingle(addresses.Z));
            if (!IsReasonable(position))
                return new PlayerLocationReadResult(
                    PlayerLocationReadStatus.UnableToRead,
                    Diagnostic: "Skyrim returned invalid coordinate values.");
            var calibration = isSolstheimMap ? SkyrimWorldMapMemoryProfile.SolstheimCalibration : attachedProfile.Calibration;
            if (!calibration.TryConvert(position, out var location))
                return new PlayerLocationReadResult(
                    PlayerLocationReadStatus.UnableToRead,
                    Diagnostic: "The player is outside the supported Skyrim world map.");

            if (session.ReadPointer(playerCharacterSingletonAddress) != playerCharacter ||
                session.ReadPointer(playerCharacter + attachedProfile.Layout.PlayerParentCellOffset) != parentCell)
                return new PlayerLocationReadResult(PlayerLocationReadStatus.UnableToRead,
                    Diagnostic: "Skyrim player or cell changed during the read.");

            return PlayerLocationReadResult.Success(location!);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogDebug(ex, "The Skyrim process could not be read; it will be rediscovered on the next poll.");
            Detach();
            return new PlayerLocationReadResult(PlayerLocationReadStatus.UnableToRead, Diagnostic: ex.Message);
        }
    }

    public void Detach()
    {
        session?.Dispose();
        session = null;
        attachedProcessId = null;
        attachedModuleBase = null;
        attachedProfile = null;
        addresses = default;
        playerCharacterSingletonAddress = default;
        lastPlayerCharacter = null;
        lastCell = null;
        lastInteriorState = null;
    }

    private static bool IsReasonable(GamePosition position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
        Math.Abs(position.X) <= 1_000_000 && Math.Abs(position.Y) <= 1_000_000 && Math.Abs(position.Z) <= 1_000_000;

    private static bool IsLikelyPointer(nint pointer)
    {
        var value = pointer.ToInt64();
        return value >= 0x10000 && value <= 0x00007FFFFFFFFFFF;
    }
}
