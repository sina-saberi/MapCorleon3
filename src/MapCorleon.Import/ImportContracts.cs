namespace MapCorleon.Import;

public sealed record MapImportRequest(string GameName, string MapJsonPath, string SpriteJsonPath);
public sealed record ImportProgress(string Stage, int Completed, int Total)
{
    public int Percentage => Total == 0 ? 0 : (int)Math.Round(Completed * 100d / Total);
}
public sealed record ImportResult(int Groups, int Categories, int Locations, int Media, string MapName);

public sealed record ImportedMap(
    long ExternalId, string Name, double StartLatitude, double StartLongitude, double InitialZoom,
    string? TilePattern, int? MinZoom, int? MaxZoom, string? SpriteImageLocation,
    IReadOnlyList<ImportedGroup> Groups, IReadOnlyList<ImportedLocation> Locations);
public sealed record ImportedGroup(long ExternalId, string Name, bool IsVisible, IReadOnlyList<ImportedCategory> Categories);
public sealed record ImportedCategory(long ExternalId, long GroupExternalId, string Name, bool IsVisible, string? IconKey, SpriteRect? Sprite);
public sealed record SpriteRect(int Width, int Height, int X, int Y, double PixelRatio);
public sealed record ImportedLocation(long ExternalId, long CategoryExternalId, string Title, string DescriptionMarkdown, double Latitude, double Longitude, IReadOnlyList<ImportedMedia> Media);
public sealed record ImportedMedia(long ExternalId, string? Title, string Url, string? FileName, string? MimeType);

public interface IMapImportSink
{
    Task<ImportResult> UpsertAsync(string gameName, ImportedMap map, IProgress<ImportProgress>? progress, CancellationToken ct);
}
public interface IMapImportProvider
{
    string Name { get; }
    Task<ImportResult> ImportAsync(MapImportRequest request, IProgress<ImportProgress>? progress = null, CancellationToken ct = default);
}
