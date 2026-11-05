using MapCorleon.Data;
using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Tests;

public sealed class TimelineServiceTests
{
    [Fact]
    public async Task EditingKeepsTimelineIdsAndDeletingTheLastTimelineCreatesADefault()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var factory = new TestDbContextFactory(options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var game = new Game { Name = "Skyrim Special Edition" };
        var map = new Map { Game = game, Name = "Skyrim" };
        var otherMap = new Map { Game = game, Name = "Solstheim" };
        db.AddRange(map, otherMap);
        await db.SaveChangesAsync();
        var service = new TimelineService(factory);

        await service.SaveAsync(map.Id, [new Timeline { Name = "First" }, new Timeline { Name = "Second" }]);
        await service.SaveAsync(otherMap.Id, [new Timeline { Name = "Unrelated" }]);
        var firstList = await service.LoadAsync(map.Id);
        firstList[0].Name = "Renamed";
        await service.SaveAsync(map.Id, firstList);
        var edited = await service.LoadAsync(map.Id);
        Assert.Equal(firstList.Select(timeline => timeline.Id), edited.Select(timeline => timeline.Id));
        Assert.Equal(new[] { "Renamed", "Second" }, edited.Select(timeline => timeline.Name));

        await service.SaveAsync(map.Id, [edited[1]]);
        Assert.Equal(edited[1].Id, Assert.Single(await service.LoadAsync(map.Id)).Id);
        await service.SaveAsync(map.Id, []);
        Assert.Equal("Default", Assert.Single(await service.LoadAsync(map.Id)).Name);
        Assert.Equal("Unrelated", Assert.Single(await service.LoadAsync(otherMap.Id)).Name);
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
