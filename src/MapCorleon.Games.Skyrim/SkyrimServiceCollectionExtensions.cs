using MapCorleon.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MapCorleon.Games.Skyrim;

public static class SkyrimServiceCollectionExtensions
{
    public static IServiceCollection AddSkyrimGame(this IServiceCollection services)
    {
        services.AddSingleton<IGameLauncher, SkyrimGameLauncher>();
        services.AddSingleton<IPlayerLocationProvider, SkyrimWorldMapPlayerLocationProvider>();
        return services;
    }
}
