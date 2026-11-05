using MapCorleon.Data;
using MapCorleon.Models;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Services;

public interface ITimelineService
{
    Task<List<Timeline>> LoadAsync(int mapId, CancellationToken ct = default);
    Task SaveAsync(int mapId, IReadOnlyList<Timeline> timelines, CancellationToken ct = default);
}

public sealed class TimelineService(IDbContextFactory<AppDbContext> factory) : ITimelineService
{
    public async Task<List<Timeline>> LoadAsync(int mapId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Timelines.AsNoTracking()
            .Where(timeline => timeline.MapId == mapId)
            .OrderBy(timeline => timeline.SortOrder)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(int mapId, IReadOnlyList<Timeline> timelines, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Timelines
            .Where(timeline => timeline.MapId == mapId)
            .ToListAsync(ct);

        var submittedIds = timelines
            .Where(timeline => timeline.Id > 0)
            .Select(timeline => timeline.Id)
            .ToHashSet();

        db.RemoveRange(existing.Where(timeline => !submittedIds.Contains(timeline.Id)));

        IReadOnlyList<Timeline> values = timelines.Count == 0
            ? [new Timeline { MapId = mapId, Name = "Default", SortOrder = 0 }]
            : timelines;

        for (var index = 0; index < values.Count; index++)
        {
            var item = values[index];
            if (item.Id == 0)
            {
                db.Timelines.Add(new Timeline
                {
                    MapId = mapId,
                    Name = item.Name,
                    SortOrder = index
                });
                continue;
            }

            var target = existing.FirstOrDefault(timeline => timeline.Id == item.Id);
            if (target is null)
                continue;

            target.Name = item.Name;
            target.SortOrder = index;
        }

        await db.SaveChangesAsync(ct);
    }
}
