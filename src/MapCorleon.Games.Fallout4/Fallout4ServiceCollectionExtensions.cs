using MapCorleon.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Games.Fallout4;

public static class Fallout4ServiceCollectionExtensions
{
    public static IServiceCollection AddFallout4Game(this IServiceCollection services)
    {
        services.AddSingleton<IPlayerLocationProvider, Fallout4PlayerLocationProvider>();
        services.AddSingleton<IGameLauncher, Fallout4GameLauncher>();
        return services;
    }

    private sealed class Fallout4GameLauncher(IGameProcessDetector detector, IProcessStarter starter)
        : ConfiguredGameLauncher(detector, starter, Fallout4GameIdentity.ProcessName, "Fallout 4")
    {
        public override string Key => "fallout4";
        public override bool Supports(GameMapDescriptor map) => Fallout4GameIdentity.IsFallout4(map);
    }
}
