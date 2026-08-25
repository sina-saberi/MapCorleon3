using MapCorleon.Data;
using MapCorleon.Contracts;
using MapCorleon.Models;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Services;

public interface IMapSettingsService
{
    Task SaveGameLaunchAsync(int mapId, GameLaunchSettings settings, CancellationToken ct = default);
}

public sealed class MapSettingsService(IDbContextFactory<AppDbContext> factory) : IMapSettingsService
{
    public async Task SaveGameLaunchAsync(int mapId, GameLaunchSettings settings, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var config = await db.MapConfigs.SingleOrDefaultAsync(value => value.MapId == mapId, ct)
            ?? throw new InvalidOperationException("The selected map has no map configuration.");
        config.LaunchExecutablePath = Normalize(settings.ExecutablePath);
        config.LaunchArguments = Normalize(settings.Arguments);
        config.LaunchWorkingDirectory = Normalize(settings.WorkingDirectory);
        await db.SaveChangesAsync(ct);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
