using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Text.Json;
using Regex = System.Text.RegularExpressions.Regex;
using Match = System.Text.RegularExpressions.Match;
using RegexOptions = System.Text.RegularExpressions.RegexOptions;
using MapCorleon.Data;
using MapCorleon.Contracts;
using MapCorleon.Models;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Services;

public sealed class EfMapImportSink(IDbContextFactory<AppDbContext> factory) : IMapImportSink
{
    public Task<ImportResult> UpsertAsync(string gameName, ImportedMap source, IProgress<ImportProgress>? progress, CancellationToken ct) =>
        UpsertAsync(gameName, source, progress, ct, null);

    public Task<ImportResult> UpsertCatalogAsync(CatalogMap entry, CancellationToken ct) =>
        UpsertAsync(entry.Game, entry.Map, null, ct, entry);

    private async Task<ImportResult> UpsertAsync(string gameName, ImportedMap source, IProgress<ImportProgress>? progress,
        CancellationToken ct, CatalogMap? entry)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        progress?.Report(new("Loading existing records", 0, source.Locations.Count));

        var maps = db.Maps.Include(x => x.Game).Include(x => x.Config).Include(x => x.Timelines);
        var map = await maps.SingleOrDefaultAsync(x => x.ExternalId == source.ExternalId && x.Config!.Provider == source.Provider, ct);
        if (entry is not null && map?.Config is { } existing && existing.CatalogRevision >= entry.Revision)
            return new(0, 0, 0, 0, map.Name, source.ExternalId);

        if (map is null)
        {
            var normalizedGameName = gameName.ToLowerInvariant();
            var game = await db.Games.FirstOrDefaultAsync(x =>
                x.CatalogName == gameName || x.Name.ToLower() == normalizedGameName, ct);
            if (game is null) { game = new Game { Name = gameName }; db.Games.Add(game); }
            else map = await maps.FirstOrDefaultAsync(x => x.GameId == game.Id && x.Name == source.Name &&
                (x.ExternalId == null || x.ExternalId == source.ExternalId) &&
                (x.Config == null || x.Config.Provider == source.Provider), ct);
            if (map is null) { map = new Map { Game = game, Name = source.Name }; db.Maps.Add(map); }
        }
        map.Name = source.Name; map.ExternalId = source.ExternalId;
        map.Config ??= new MapConfig { Map = map };
        map.Config.Provider = source.Provider;
        map.Config.DefaultZoom = source.InitialZoom; map.Config.TilePattern = source.TilePattern;
        map.Config.MinZoom = source.MinZoom; map.Config.MaxZoom = source.MaxZoom;
        map.Config.TileBoundsJson = source.TileBounds is null ? null : JsonSerializer.Serialize(source.TileBounds);
        map.Config.SpriteImagePath = source.SpriteImageLocation ?? map.Config.SpriteImagePath;
        if (entry is not null)
        {
            map.Game!.CatalogName = gameName;
            map.Config.CatalogRevision = entry.Revision;
            map.Config.SourcePageUrl = entry.SourcePageUrl;
            map.Config.GameImage = entry.GameImage;
            map.Config.MapImage = entry.MapImage;
        }
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
            group.ExternalId = importedGroup.ExternalId; group.Name = importedGroup.Name;
            if (group.Id == 0) { group.IsVisible = importedGroup.IsVisible; db.Groups.Add(group); }
            groupEntities[importedGroup.ExternalId] = group;
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
                category.ExternalId = importedCategory.ExternalId; category.Name = importedCategory.Name;
                category.IconKey = importedCategory.IconKey;
                if (importedCategory.Sprite is { } sprite)
                {
                    category.IconX = sprite.X; category.IconY = sprite.Y;
                    category.IconWidth = sprite.Width; category.IconHeight = sprite.Height; category.IconPixelRatio = sprite.PixelRatio;
                }
                if (entry is not null)
                {
                    category.IconLayersJson = importedCategory.IconLayers is { Count: > 0 } layers
                        ? JsonSerializer.Serialize(layers) : null;
                    category.IconFont = entry.IconFont;
                    category.IconAtlas = entry.Map.SpriteImageLocation;
                }
                if (category.Id == 0) { category.IsVisible = importedCategory.IsVisible; db.Categories.Add(category); }
                categories[importedCategory.ExternalId] = category;
            }
        await db.SaveChangesAsync(ct);

        var existingLocations = await db.Locations.Include(x => x.Media)
            .Where(x => x.Category!.Group!.MapId == map.Id && !x.IsCustom && x.TimelineId == null).ToListAsync(ct);
        var existingById = existingLocations.Where(x => x.ExternalId.HasValue).ToLookup(x => x.ExternalId!.Value);
        var legacyByCategory = existingLocations.Where(x => x.ExternalId is null).ToLookup(x => x.CategoryId);
        var importedLocations = new Dictionary<long, Location>();
        var mediaCount = 0; var completed = 0;
        foreach (var imported in source.Locations)
        {
            ct.ThrowIfCancellationRequested();
            if (!categories.TryGetValue(imported.CategoryExternalId, out var category)) continue;
            var matches = existingById[imported.ExternalId];
            var location = matches.FirstOrDefault(x => x.CategoryId == category.Id) ?? matches.SingleOrDefault()
                ?? legacyByCategory[category.Id].FirstOrDefault(x => x.ExternalId is null && x.Title == imported.Title &&
                    x.Latitude == imported.Latitude && x.Longitude == imported.Longitude)
                ?? new Location { Title = imported.Title };
            importedLocations.Add(imported.ExternalId, location);
            location.ExternalId = imported.ExternalId;
            location.CategoryId = category.Id;
            if (location.Id == 0) db.Locations.Add(location);
            location.Title = imported.Title; location.DescriptionMarkdown = imported.DescriptionMarkdown;
            location.Latitude = imported.Latitude; location.Longitude = imported.Longitude; location.IsCustom = false;
            foreach (var importedMedia in imported.Media)
            {
                var media = location.Media.FirstOrDefault(x => x.ExternalId == importedMedia.ExternalId)
                    ?? location.Media.FirstOrDefault(x => x.ExternalId is null && x.RelativePath == importedMedia.Source);
                if (media is null) { media = new Media { ExternalId = importedMedia.ExternalId, ContentHash = Hash(importedMedia.Source), RelativePath = importedMedia.Source }; location.Media.Add(media); }
                media.ExternalId = importedMedia.ExternalId;
                media.Caption = importedMedia.Title;
                media.SourceUrl = importedMedia.Source;
                if (!File.Exists(media.RelativePath))
                {
                    media.RelativePath = importedMedia.Source;
                    media.ContentHash = Hash(importedMedia.Source);
                }
                mediaCount++;
            }
            completed++;
            if (progress is not null && completed % 100 == 0)
            {
                await db.SaveChangesAsync(ct);
                progress.Report(new("Saving locations", completed, source.Locations.Count));
            }
        }
        await db.SaveChangesAsync(ct);

        // MapGenie descriptions can link to another marker using MapGenie's external location ID.
        // Rewrite those links only after inserts have assigned our stable database IDs.
        var localIds = existingById.ToDictionary(x => x.Key, x => x.First().Id);
        foreach (var (externalId, location) in importedLocations) localIds[externalId] = location.Id;
        foreach (var location in importedLocations.Values)
            location.DescriptionMarkdown = RewriteLocationLinks(location.DescriptionMarkdown, localIds);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        progress?.Report(new("Import complete", source.Locations.Count, source.Locations.Count));
        return new(source.Groups.Count, categories.Count, source.Locations.Count, mediaCount, source.Name, source.ExternalId);
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
