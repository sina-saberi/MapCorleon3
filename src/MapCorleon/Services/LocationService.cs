using MapCorleon.Data;
using MapCorleon.Models;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Services;

public interface ILocationService
{
    Task<List<Location>> LoadAsync(int mapId, int timelineId, CancellationToken ct = default);
    Task ToggleMarkedAsync(int locationId, int timelineId, CancellationToken ct = default);
    Task<Location> SaveAsync(Location location, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task AddMediaAsync(int locationId, string file, CancellationToken ct = default);
}

public sealed class LocationService(
    AppDbContext db,
    IMediaService mediaService,
    IMediaPathResolver mediaPathResolver) : ILocationService
{
    public async Task<List<Location>> LoadAsync(int mapId, int timelineId, CancellationToken ct = default)
    {
        var locations = await db.Locations
            .AsNoTracking()
            .Include(location => location.Category)
                .ThenInclude(category => category!.Group)
            .Include(location => location.Media)
            .Include(location => location.Marks.Where(mark => mark.TimelineId == timelineId))
            .Where(location =>
                location.Category!.Group!.MapId == mapId &&
                (!location.IsCustom || location.TimelineId == timelineId))
            .ToListAsync(ct);

        foreach (var media in locations.SelectMany(location => location.Media))
            media.RelativePath = mediaPathResolver.Resolve(media.RelativePath);

        return locations;
    }

    public async Task ToggleMarkedAsync(int locationId, int timelineId, CancellationToken ct = default)
    {
        var mark = await db.MarkedLocations.SingleOrDefaultAsync(
            item => item.LocationId == locationId && item.TimelineId == timelineId,
            ct);

        if (mark is null)
            db.Add(new MarkedLocation { LocationId = locationId, TimelineId = timelineId });
        else
            db.Remove(mark);

        await db.SaveChangesAsync(ct);
    }

    public async Task<Location> SaveAsync(Location location, CancellationToken ct = default)
    {
        if (location.Id == 0)
        {
            db.Add(location);
        }
        else
        {
            var target = await db.Locations.FindAsync([location.Id], ct)
                ?? throw new InvalidOperationException("Location no longer exists.");

            target.Title = location.Title;
            target.DescriptionMarkdown = location.DescriptionMarkdown;
            target.CategoryId = location.CategoryId;
            target.TimelineId = location.TimelineId;
            target.Latitude = location.Latitude;
            target.Longitude = location.Longitude;
            target.IsCustom = location.IsCustom;
        }

        await db.SaveChangesAsync(ct);
        return location;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var location = await db.Locations.FindAsync([id], ct);
        if (location is null)
            return;

        db.Remove(location);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddMediaAsync(int locationId, string file, CancellationToken ct = default)
    {
        db.Media.Add(await mediaService.StoreAsync(locationId, file, ct));
        await db.SaveChangesAsync(ct);
    }
}
