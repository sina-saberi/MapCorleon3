using MapCorleon.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Games.Kcd2;

public static class Kcd2ServiceCollectionExtensions
{
    public static IServiceCollection AddKcd2Game(this IServiceCollection services)
    {
        services.AddSingleton<IPlayerLocationProvider, Kcd2PlayerLocationProvider>();
        services.AddSingleton<IGameLauncher, Kcd2Launcher>();
        return services;
    }

    private sealed class Kcd2Launcher(IGameProcessDetector detector, IProcessStarter starter)
        : ConfiguredGameLauncher(detector, starter, Kcd2MapProfile.ProcessName, Kcd2MapProfile.GameName)
    {
        public override string Key => "kcd2";
        public override bool Supports(GameMapDescriptor map) => Kcd2MapProfile.IsKcd2(map);
    }
}
