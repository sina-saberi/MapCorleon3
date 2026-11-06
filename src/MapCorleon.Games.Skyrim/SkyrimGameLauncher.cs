using MapCorleon.Contracts;

namespace MapCorleon.Games.Skyrim;

public sealed class SkyrimGameLauncher(IGameProcessDetector processDetector, IProcessStarter processStarter)
    : ConfiguredGameLauncher(processDetector, processStarter, SkyrimWorldMapPlayerLocationProvider.TargetProcessName, "Skyrim")
{
    public override string Key => "skyrim-se";
    public override bool Supports(GameMapDescriptor map) => SkyrimGameIdentity.IsSkyrim(map);
}
