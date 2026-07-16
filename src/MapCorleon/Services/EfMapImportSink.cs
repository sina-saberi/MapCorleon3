using System.Security.Cryptography;
using System.Text;
using Regex = System.Text.RegularExpressions.Regex;
using Match = System.Text.RegularExpressions.Match;
using RegexOptions = System.Text.RegularExpressions.RegexOptions;
using MapCorleon.Data;
using MapCorleon.Import;
using MapCorleon.Models;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Services;

public sealed class EfMapImportSink(IDbContextFactory<AppDbContext> factory) : IMapImportSink
{
    public async Task<ImportResult> UpsertAsync(string gameName, ImportedMap source, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        progress?.Report(new("Loading existing records", 0, source.Locations.Count));

        var normalizedGameName = gameName.ToLower();
        var game = await db.Games.FirstOrDefaultAsync(x => x.Name.ToLower() == normalizedGameName, ct);
        if (game is null) { game = new Game { Name = gameName }; db.Games.Add(game); }
        else game.Name = gameName;

        var map = await db.Maps.Include(x => x.Config).Include(x => x.Timelines)
            .FirstOrDefaultAsync(x => x.GameId == game.Id && (x.ExternalId == source.ExternalId || x.Name == source.Name), ct);
        if (map is null) { map = new Map { Game = game, Name = source.Name, ExternalId = source.ExternalId }; db.Maps.Add(map); }
        map.Name = source.Name; map.ExternalId = source.ExternalId;
        map.Config ??= new MapConfig { Map = map };
        map.Config.DefaultZoom = source.InitialZoom; map.Config.TilePattern = source.TilePattern;
        map.Config.MinZoom = source.MinZoom; map.Config.MaxZoom = source.MaxZoom; map.Config.SpriteImagePath = source.SpriteImageLocation;
        map.Config.MinLatitude = source.Locations.Count == 0 ? source.StartLatitude : source.Locations.Min(x => x.Latitude);
        map.Config.MaxLatitude = source.Locations.Count == 0 ? source.StartLatitude : source.Locations.Max(x => x.Latitude);
        map.Config.MinLongitude = source.Locations.Count == 0 ? source.StartLongitude : source.Locations.Min(x => x.Longitude);
        map.Config.MaxLongitude = source.Locations.Count == 0 ? source.StartLongitude : source.Locations.Max(x => x.Longitude);
        if (map.Timelines.Count == 0) map.Timelines.Add(new Timeline { Name = "Default", SortOrder = 0 });
        await db.SaveChangesAsync(ct);

        var existingGroups = await db.Groups.Where(x => x.MapId == map.Id).ToListAsync(ct);
        var groupsByExternalId = existingGroups.Where(x => x.ExternalId.HasValue).ToDictionary(x => x.ExternalId!.Value);
        var groupEntities = new Dictionary<long, Group>();
        foreach (var importedGroup in source.Groups)
        {
            if (!groupsByExternalId.TryGetValue(importedGroup.ExternalId, out var group))
                group = existingGroups.FirstOrDefault(x => x.ExternalId is null && x.Name == importedGroup.Name)
                    ?? new Group { MapId = map.Id, Name = importedGroup.Name };
            group.ExternalId = importedGroup.ExternalId; group.Name = importedGroup.Name; group.IsVisible = importedGroup.IsVisible;
            if (group.Id == 0) db.Groups.Add(group); groupEntities[importedGroup.ExternalId] = group;
        }
        await db.SaveChangesAsync(ct);

        var groupIds = groupEntities.Values.Select(x => x.Id).ToList();
        var existingCategories = await db.Categories.Where(x => groupIds.Contains(x.GroupId)).ToListAsync(ct);
        var categories = new Dictionary<long, Category>();
        foreach (var importedGroup in source.Groups)
        foreach (var importedCategory in importedGroup.Categories)
        {
            var group = groupEntities[importedGroup.ExternalId];
            var category = existingCategories.FirstOrDefault(x => x.GroupId == group.Id && x.ExternalId == importedCategory.ExternalId)
                ?? existingCategories.FirstOrDefault(x => x.GroupId == group.Id && x.ExternalId is null && x.Name == importedCategory.Name)
                ?? new Category { GroupId = group.Id, Name = importedCategory.Name };
            category.ExternalId = importedCategory.ExternalId; category.Name = importedCategory.Name; category.IsVisible = importedCategory.IsVisible;
            category.IconKey = importedCategory.IconKey; category.IconX = importedCategory.Sprite?.X; category.IconY = importedCategory.Sprite?.Y;
            category.IconWidth = importedCategory.Sprite?.Width; category.IconHeight = importedCategory.Sprite?.Height; category.IconPixelRatio = importedCategory.Sprite?.PixelRatio;
            if (category.Id == 0) db.Categories.Add(category); categories[importedCategory.ExternalId] = category;
        }
        await db.SaveChangesAsync(ct);

        var categoryIds = categories.Values.Select(x => x.Id).ToList();
        var existingLocations = await db.Locations.Include(x => x.Media).Where(x => categoryIds.Contains(x.CategoryId)).ToListAsync(ct);
        var locationLookup = existingLocations.Where(x => x.ExternalId.HasValue).ToDictionary(x => (x.CategoryId, x.ExternalId!.Value));
        var mediaCount = 0; var completed = 0;
        foreach (var imported in source.Locations)
        {
            ct.ThrowIfCancellationRequested();
            if (!categories.TryGetValue(imported.CategoryExternalId, out var category)) continue;
            if (!locationLookup.TryGetValue((category.Id, imported.ExternalId), out var location))
            {
                location = existingLocations.FirstOrDefault(x => x.CategoryId == category.Id && x.ExternalId is null && x.Title == imported.Title && x.Latitude == imported.Latitude && x.Longitude == imported.Longitude)
                    ?? new Location { CategoryId = category.Id, Title = imported.Title };
                location.ExternalId = imported.ExternalId;
                if (location.Id == 0) db.Locations.Add(location); locationLookup[(category.Id, imported.ExternalId)] = location;
            }
            location.Title = imported.Title; location.DescriptionMarkdown = imported.DescriptionMarkdown;
            location.Latitude = imported.Latitude; location.Longitude = imported.Longitude; location.IsCustom = false;
            foreach (var importedMedia in imported.Media)
            {
                var media = location.Media.FirstOrDefault(x => x.ExternalId == importedMedia.ExternalId)
                    ?? location.Media.FirstOrDefault(x => x.ExternalId is null && x.ContentHash == Hash(importedMedia.Url));
                if (media is null) { media = new Media { ExternalId = importedMedia.ExternalId, ContentHash = Hash(importedMedia.Url), RelativePath = importedMedia.Url }; location.Media.Add(media); }
                media.ExternalId = importedMedia.ExternalId;
                media.Caption = importedMedia.Title; media.RelativePath = importedMedia.Url; media.ContentHash = Hash(importedMedia.Url); mediaCount++;
            }
            completed++;
            if (completed % 100 == 0) { await db.SaveChangesAsync(ct); progress?.Report(new("Saving locations", completed, source.Locations.Count)); }
        }
        await db.SaveChangesAsync(ct);

        // MapGenie descriptions can link to another marker using MapGenie's external location ID.
        // Rewrite those links only after inserts have assigned our stable database IDs.
        var localIds = locationLookup.ToDictionary(x => x.Key.Item2, x => x.Value.Id);
        foreach (var location in locationLookup.Values.Distinct())
            location.DescriptionMarkdown = RewriteLocationLinks(location.DescriptionMarkdown, localIds);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        progress?.Report(new("Import complete", source.Locations.Count, source.Locations.Count));
        return new(source.Groups.Count, categories.Count, source.Locations.Count, mediaCount, source.Name);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string RewriteLocationLinks(string text, IReadOnlyDictionary<long, int> localIds)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = Regex.Replace(text, @"https?://[^\s\)\]]*[?&](?:location|locations|location_ids|locationIds)=(?<id>\d+)[^\s\)\]]*", Replace, RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"https?://[^\s\)\]]*/locations?/(?<id>\d+)[^\s\)\]]*", Replace, RegexOptions.IgnoreCase);

        string Replace(Match match) => long.TryParse(match.Groups["id"].Value, out var externalId) && localIds.TryGetValue(externalId, out var localId)
            ? $"mapcorleon://location/{localId}" : match.Value;
    }
}
