using MapCorleon.Data;
using MapCorleon.Contracts;
using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Tests;

public sealed class MapSettingsServiceTests
{
    [Fact]
    public async Task SavesLaunchSettingsOnlyForSelectedMap()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var game = new Game { Name = "Skyrim Special Edition" };
            var skyrim = new Map { Name = "Skyrim", Game = game };
            skyrim.Config = NewConfig(skyrim);
            var solstheim = new Map { Name = "Solstheim", Game = game };
            solstheim.Config = NewConfig(solstheim);
            game.Maps.Add(skyrim);
            game.Maps.Add(solstheim);
            setup.Add(game);
            await setup.SaveChangesAsync();
        }

        await using (var readIds = new AppDbContext(options))
        {
            var skyrimId = await readIds.Maps
                .Where(map => map.Name == "Skyrim")
                .Select(map => map.Id)
                .SingleAsync();
            var service = new MapSettingsService(new TestDbContextFactory(options));
            await service.SaveGameLaunchAsync(skyrimId, new GameLaunchSettings(
                " C:\\Games\\SkyrimSE.exe ", " -forcesteamloader ", " C:\\Games "));
        }

        await using var verify = new AppDbContext(options);
        var configs = await verify.MapConfigs
            .Include(config => config.Map)
            .OrderBy(config => config.Map!.Name)
            .ToListAsync();
        var skyrimConfig = configs.Single(config => config.Map!.Name == "Skyrim");
        var solstheimConfig = configs.Single(config => config.Map!.Name == "Solstheim");

        Assert.Equal("C:\\Games\\SkyrimSE.exe", skyrimConfig.LaunchExecutablePath);
        Assert.Equal("-forcesteamloader", skyrimConfig.LaunchArguments);
        Assert.Equal("C:\\Games", skyrimConfig.LaunchWorkingDirectory);
        Assert.Null(solstheimConfig.LaunchExecutablePath);
        Assert.Null(solstheimConfig.LaunchArguments);
        Assert.Null(solstheimConfig.LaunchWorkingDirectory);
    }

    private static MapConfig NewConfig(Map map) => new()
    {
        Map = map,
        MinLatitude = -85,
        MaxLatitude = 85,
        MinLongitude = -180,
        MaxLongitude = 180
    };

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
