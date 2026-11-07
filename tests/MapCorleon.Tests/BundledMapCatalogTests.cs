using MapCorleon.Contracts;
using MapCorleon.Data;
using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Text.Json;

namespace MapCorleon.Tests;

public sealed class BundledMapCatalogTests
{
    [Fact]
    public async Task PublishedCatalogSeedsAllMapsAndReferencesLocalAssets()
    {
        var entries = Directory.EnumerateFiles(BundledMapCatalog.Root, "*.map.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<CatalogMap>(File.ReadAllText(path))!).ToArray();
        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            Assert.NotNull(BundledMapCatalog.Asset(entry.GameImage));
            Assert.NotNull(BundledMapCatalog.Asset(entry.MapImage));
            Assert.NotNull(BundledMapCatalog.Asset(entry.IconFont ?? entry.Map.SpriteImageLocation));
        }

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await DbSchemaInitializer.InitializeAsync(db);
        var catalog = new BundledMapCatalog(new EfMapImportSink(factory), new AppLogService(factory));
        await catalog.SeedAsync();
        await catalog.SeedAsync();

        Assert.Empty(await db.AppLogs.ToListAsync());
        Assert.Equal(entries.Length, await db.Maps.CountAsync());
        Assert.Equal(entries.Length, await db.Timelines.CountAsync());
        Assert.Equal(entries.Sum(entry => entry.Map.Locations.Count), await db.Locations.CountAsync());
        Assert.Empty(await db.MarkedLocations.ToListAsync());
    }

    [Fact]
    public async Task CatalogRevisionUpdatesInPlaceAndPreservesProgressAndCustomData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var factory = new TestDbContextFactory(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await using (var db = factory.CreateDbContext())
            await DbSchemaInitializer.InitializeAsync(db);

        var sink = new EfMapImportSink(factory);
        var original = Entry(1, "Landmark", categoryId: 10, addOther: true);
        await sink.UpsertCatalogAsync(original, default);

        int mapId, timelineId, locationId, customId;
        await using (var db = factory.CreateDbContext())
        {
            var map = await db.Maps.Include(item => item.Config).SingleAsync();
            mapId = map.Id;
            map.Config!.LaunchExecutablePath = @"C:\Games\Game.exe";
            var timeline = await db.Timelines.SingleAsync();
            timelineId = timeline.Id;
            timeline.Name = "My playthrough";
            var landmark = await db.Locations.SingleAsync(item => item.ExternalId == 100);
            locationId = landmark.Id;
            var custom = new Location
            {
                Title = landmark.Title, Latitude = landmark.Latitude, Longitude = landmark.Longitude,
                CategoryId = landmark.CategoryId, TimelineId = timeline.Id, IsCustom = true
            };
            db.AddRange(custom, new MarkedLocation { LocationId = locationId, TimelineId = timelineId },
                new Pin { LocationId = locationId, TimelineId = timelineId });
            await db.SaveChangesAsync();
            customId = custom.Id;
        }

        await sink.UpsertCatalogAsync(Entry(1, "Should be skipped", 10), default);
        await using (var db = factory.CreateDbContext())
            Assert.Equal("Landmark", (await db.Locations.FindAsync(locationId))!.Title);

        await sink.UpsertCatalogAsync(Entry(2, "Updated landmark", categoryId: 20), default);
        await using (var db = factory.CreateDbContext())
        {
            var map = await db.Maps.Include(item => item.Config).SingleAsync();
            Assert.Equal(mapId, map.Id);
            Assert.Equal(2, map.Config!.CatalogRevision);
            Assert.Equal(@"C:\Games\Game.exe", map.Config.LaunchExecutablePath);
            var updated = await db.Locations.Include(item => item.Category).SingleAsync(item => item.Id == locationId);
            Assert.Equal("Updated landmark", updated.Title);
            Assert.Equal(20, updated.Category!.ExternalId);
            Assert.True((await db.Locations.FindAsync(customId))!.IsCustom);
            Assert.True(await db.Locations.AnyAsync(item => item.ExternalId == 101));
            Assert.Equal(locationId, (await db.MarkedLocations.SingleAsync()).LocationId);
            Assert.Equal(locationId, (await db.Pins.SingleAsync()).LocationId);
            Assert.Equal("My playthrough", (await db.Timelines.FindAsync(timelineId))!.Name);
        }

        var percentages = await new TimelineService(factory).GetPercentagesAsync(mapId);
        Assert.Equal(33, percentages[timelineId]);
    }

    private static CatalogMap Entry(int revision, string title, long categoryId, bool addOther = false)
    {
        List<ImportedLocation> locations = [new(100, categoryId, title, "", 5, 10, [])];
        if (addOther) locations.Add(new(101, categoryId, "Omitted on refresh", "", 6, 12, []));
        return new("Test game", revision,
            new("mapgenie", 50, "Test map", 0, 0, 3, null, null, null, null, null,
                [new(1, "Group", true, [new(categoryId, 1, "Category", true, null, null)])], locations));
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
