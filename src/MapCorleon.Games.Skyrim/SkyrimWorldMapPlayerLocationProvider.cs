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
    private SkyrimWorldMapMemoryProfile? attachedProfile;

    public string Key => "skyrim-se:skyrim-world";
    public PlayerLocationProviderMetadata Metadata { get; } = new(
        "SKYRIM PLAYER TRACKING",
        "These launch settings belong only to the selected Skyrim map. Tracking always attaches to SkyrimSE.exe.");

    public bool Supports(GameMapDescriptor map) => SkyrimGameIdentity.IsWorldMap(map);

    public Task<PlayerLocationReadResult> ReadAsync(GameMapDescriptor map, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Supports(map))
            return Task.FromResult(new PlayerLocationReadResult(
                PlayerLocationReadStatus.UnableToRead,
                Diagnostic: "The selected map is not supported."));

        var process = processDetector.Find(TargetProcessName);
        if (process is null)
        {
            Detach();
            return Task.FromResult(new PlayerLocationReadResult(PlayerLocationReadStatus.GameNotRunning));
        }

        try
        {
            if (attachedProcessId != process.Id || attachedModuleBase != process.ModuleBaseAddress)
            {
                Detach();
                var profile = Profiles.FirstOrDefault(candidate => candidate.SupportsVersion(process.FileVersion));
                if (profile is null)
                    return Task.FromResult(new PlayerLocationReadResult(
                        PlayerLocationReadStatus.UnsupportedGameVersion,
                        Diagnostic: $"SkyrimSE.exe {process.FileVersion} is not supported."));

                session = memoryReader.Open(process.Id);
                attachedProcessId = process.Id;
                attachedModuleBase = process.ModuleBaseAddress;
                attachedProfile = profile;
                addresses = profile.GetPositionAddresses(process.ModuleBaseAddress);
            }

            var position = new GamePosition(
                session!.ReadSingle(addresses.X),
                session.ReadSingle(addresses.Y),
                session.ReadSingle(addresses.Z));
            if (!IsReasonable(position))
                return Task.FromResult(new PlayerLocationReadResult(
                    PlayerLocationReadStatus.UnableToRead,
                    Diagnostic: "Skyrim returned invalid coordinate values."));
            if (!attachedProfile!.Calibration.TryConvert(position, out var location))
                return Task.FromResult(new PlayerLocationReadResult(
                    PlayerLocationReadStatus.UnableToRead,
                    Diagnostic: "The player is outside the supported Skyrim world map."));

            return Task.FromResult(PlayerLocationReadResult.Success(location!));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogDebug(ex, "The Skyrim process could not be read; it will be rediscovered on the next poll.");
            Detach();
            return Task.FromResult(new PlayerLocationReadResult(PlayerLocationReadStatus.UnableToRead, Diagnostic: ex.Message));
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
    }

    private static bool IsReasonable(GamePosition position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
        Math.Abs(position.X) <= 1_000_000 && Math.Abs(position.Y) <= 1_000_000 && Math.Abs(position.Z) <= 1_000_000;
}
