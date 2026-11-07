using System.IO;
using MapCorleon.Contracts;

namespace MapCorleon.Services;

internal sealed class Kcd2MonasteryMapProvider(ISettingsService settings) : IMapProvider
{
    private const string TileHost = "kcd2-local.invalid";
    private const string TileSet = "sedletz-v1";
    private static readonly IReadOnlyDictionary<int, TileRange> Bounds = new Dictionary<int, TileRange>
    {
        [10] = new(512, 512, 511, 511),
        [11] = new(1024, 1025, 1022, 1023)
    };

    public string Key => "kcd2-local";
    public string DisplayName => "KCD2 Sedletz Monastery";

    public Task<byte[]?> DownloadTileAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(TileHost, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<byte[]?>(null);

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not [TileSet, var zText, var xText, var yText] ||
            !int.TryParse(zText, out var z) || !int.TryParse(xText, out var x) || !int.TryParse(yText, out var y) ||
            !Bounds.TryGetValue(z, out var range) ||
            x < range.MinX || x > range.MaxX || y < range.MinY || y > range.MaxY)
            return Task.FromResult<byte[]?>(null);

        return ReadTileAsync(TilePath(TileDirectory, z, x, y), ct);
    }

    private string TileDirectory => Path.Combine(settings.Current.StorageDirectory, Key, TileSet);

    private static async Task<byte[]?> ReadTileAsync(string path, CancellationToken ct) =>
        File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;

    private static string TilePath(string directory, int z, int x, int y) => Path.Combine(directory, $"{z}-{x}-{y}.png");
}
