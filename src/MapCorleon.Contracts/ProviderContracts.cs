namespace MapCorleon.Contracts;

public sealed record CatalogMap(string Game, int Revision, ImportedMap Map, string? GameImage = null,
    string? MapImage = null, string? IconFont = null, string? SourcePageUrl = null);
public sealed record ImportedMap(
    string Provider, long ExternalId, string Name, double StartLatitude, double StartLongitude, double InitialZoom,
    string? TilePattern, int? MinZoom, int? MaxZoom, IReadOnlyDictionary<int, TileRange>? TileBounds, string? SpriteImageLocation,
    IReadOnlyList<ImportedGroup> Groups, IReadOnlyList<ImportedLocation> Locations);
public sealed record TileRange(int MinX, int MaxX, int MinY, int MaxY);
public sealed record ImportedGroup(long ExternalId, string Name, bool IsVisible, IReadOnlyList<ImportedCategory> Categories);
public sealed record ImportedCategory(long ExternalId, long GroupExternalId, string Name, bool IsVisible, string? IconKey, SpriteRect? Sprite)
{
    public IReadOnlyList<ImportedIconLayer>? IconLayers { get; init; }
}
public sealed record ImportedIconLayer(int CodePoint, string? Color = null);
public sealed record SpriteRect(int Width, int Height, int X, int Y, double PixelRatio);
public sealed record ImportedLocation(long ExternalId, long CategoryExternalId, string Title, string DescriptionMarkdown, double Latitude, double Longitude, IReadOnlyList<ImportedMedia> Media);
public sealed record ImportedMedia(long ExternalId, string? Title, string Source);

public interface IProviderStorage
{
    Task<StoredAsset> CacheAsync(string provider, string kind, string source, string? preferredFileName = null,
        CancellationToken ct = default, string? referrer = null);
}
public sealed record StoredAsset(string FullPath, string ContentHash);

public interface IMapProvider
{
    string Key { get; }
    string DisplayName { get; }
    Task<byte[]?> DownloadTileAsync(string url, CancellationToken ct = default);
}
public interface IMapProviderResolver
{
    IReadOnlyList<IMapProvider> All { get; }
    IMapProvider Get(string key);
}
