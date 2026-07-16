namespace MapCorleon.Contracts;

public sealed record MapImportRequest(string Provider, string PageUrl, bool DownloadMedia, bool DownloadTiles);
public sealed record ImportProgress(string Stage, int Completed, int Total)
{
    public int Percentage => Total == 0 ? 0 : (int)Math.Round(Completed * 100d / Total);
}
public sealed record ImportResult(int Groups, int Categories, int Locations, int Media, string MapName, long MapExternalId);

public sealed record ImportedMap(
    string Provider, long ExternalId, string Name, double StartLatitude, double StartLongitude, double InitialZoom,
    string? TilePattern, int? MinZoom, int? MaxZoom, IReadOnlyDictionary<int, TileRange>? TileBounds, string? SpriteImageLocation,
    IReadOnlyList<ImportedGroup> Groups, IReadOnlyList<ImportedLocation> Locations);
public sealed record TileRange(int MinX, int MaxX, int MinY, int MaxY);
public sealed record ImportedGroup(long ExternalId, string Name, bool IsVisible, IReadOnlyList<ImportedCategory> Categories);
public sealed record ImportedCategory(long ExternalId, long GroupExternalId, string Name, bool IsVisible, string? IconKey, SpriteRect? Sprite);
public sealed record SpriteRect(int Width, int Height, int X, int Y, double PixelRatio);
public sealed record ImportedLocation(long ExternalId, long CategoryExternalId, string Title, string DescriptionMarkdown, double Latitude, double Longitude, IReadOnlyList<ImportedMedia> Media);
public sealed record ImportedMedia(long ExternalId, string? Title, string Source);

public interface IProviderStorage
{
    Task<StoredAsset> CacheAsync(string provider, string kind, string source, string? preferredFileName = null,
        CancellationToken ct = default, string? referrer = null);
}
public sealed record StoredAsset(string FullPath, string ContentHash);

public interface IMapImportSink
{
    Task<ImportResult> UpsertAsync(string gameName, ImportedMap map, IProgress<ImportProgress>? progress, CancellationToken ct);
}
public interface IMapProvider
{
    string Key { get; }
    string DisplayName { get; }
    Task<ImportResult> ImportAsync(MapImportRequest request, IProgress<ImportProgress>? progress = null, CancellationToken ct = default);
    Task<byte[]?> DownloadTileAsync(string url, CancellationToken ct = default);
}
public interface IMapProviderResolver
{
    IReadOnlyList<IMapProvider> All { get; }
    IMapProvider Get(string key);
}
