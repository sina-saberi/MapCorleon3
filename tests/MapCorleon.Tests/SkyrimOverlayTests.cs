using MapCorleon.Contracts;
using MapCorleon.Data;
using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Tests;

public sealed class SkyrimOverlayTests
{
    [Fact]
    public void NearbyCardsAreLimitedAndDismissedUntilThePlayerLeaves()
    {
        var nearby = new OverlayNearbyLocations();
        var player = new PlayerLocation(0, 0, 0, 0, 0);
        Location[] locations =
        [
            new() { Id = 1, Title = "Closest", Longitude = .001 },
            new() { Id = 2, Title = "Second", Longitude = .002 },
            new() { Id = 3, Title = "Third", Longitude = .0025 },
            new() { Id = 4, Title = "Fourth", Longitude = .003 },
            new() { Id = 5, Title = "Already collected", Marks = [new MarkedLocation()] },
            new() { Id = 6, Title = "Distant", Longitude = .006 },
        ];

        Assert.Equal(new[] { 1, 2, 3 }, nearby.Find(player, locations).Select(card => card.Id));
        nearby.Dismiss(1);
        Assert.Equal(new[] { 2, 3, 4 }, nearby.Find(player, locations).Select(card => card.Id));
        Assert.Empty(nearby.Find(null, locations));
        Assert.DoesNotContain(nearby.Find(player with { Longitude = -.0027 }, locations), card => card.Id == 1);
        Assert.DoesNotContain(nearby.Find(player, locations), card => card.Id == 1);
        nearby.Find(player with { Longitude = -.005 }, locations);
        Assert.Equal(1, nearby.Find(player, locations)[0].Id);
    }

    [Fact]
    public void NearbyCardsRespectTheGameRadiusAndCategoryPreferences()
    {
        var nearby = new OverlayNearbyLocations();
        var player = new PlayerLocation(0, 0, 0, 0, 0);
        Location[] locations =
        [
            new() { Id = 1, Title = "Cave", Longitude = .001, Category = new Category { Name = "Caves" } },
            new() { Id = 2, Title = "Chest", Longitude = .0015, Category = new Category { Name = "Loot" } },
            new() { Id = 3, Title = "Ruin", Longitude = .003, Category = new Category { Name = "Ruins" } },
        ];

        var preferences = new GameOverlaySettings { NearbyRadius = 1250, ExcludedCategories = ["caves"] };
        Assert.Equal(2, Assert.Single(nearby.Find(player, locations, preferences)).Id);
        Assert.Equal(3, nearby.Find(player, locations).Count);
    }

    [Fact]
    public async Task CollectingTwiceKeepsOneMarkOnTheRequestedTimeline()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var game = new Game { Name = "Skyrim Special Edition" };
        var map = new Map { Name = "Skyrim", Game = game };
        var otherMap = new Map { Name = "Solstheim", Game = game };
        var timeline = new Timeline { Name = "Main", Map = map };
        var otherTimeline = new Timeline { Name = "Alternate", Map = map };
        var location = new Location
        {
            Title = "Cave",
            IsCustom = true,
            Timeline = timeline,
            Category = new Category { Name = "Caves", Group = new Group { Name = "Places", Map = map } }
        };
        db.AddRange(location, otherTimeline, otherMap);
        await db.SaveChangesAsync();

        var service = new LocationService(db, new TestDbContextFactory(options), null!, null!);
        Assert.Null(await service.MarkCollectedAsync(location.Id, map.Id, otherTimeline.Id));
        Assert.NotNull(await service.MarkCollectedAsync(location.Id, map.Id, timeline.Id));
        Assert.NotNull(await service.MarkCollectedAsync(location.Id, map.Id, timeline.Id));
        Assert.Null(await service.MarkCollectedAsync(location.Id, otherMap.Id, timeline.Id));

        var mark = Assert.Single(await db.MarkedLocations.ToListAsync());
        Assert.Equal(timeline.Id, mark.TimelineId);
        Assert.Equal(location.Id, mark.LocationId);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
