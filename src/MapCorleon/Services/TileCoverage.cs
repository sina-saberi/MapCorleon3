using System.Text.Json;
using MapCorleon.Contracts;
using MapCorleon.Models;

namespace MapCorleon.Services;

/// <summary>
/// Resolves the only tile coordinates that a map is allowed to request.
/// Both the map renderer and the offline downloader use this plan so their
/// zoom levels and bounds cannot drift apart.
/// </summary>
public sealed record TileCoverage(IReadOnlyDictionary<int, TileRange> Ranges)
{
    public int MinZoom => Ranges.Keys.Min();
    public int MaxZoom => Ranges.Keys.Max();
    public IReadOnlyList<int> ZoomLevels => Ranges.Keys.Order().ToList();

    public static TileCoverage? Create(MapConfig config)
    {
        if (config.MinZoom is not int configuredMin || config.MaxZoom is not int configuredMax ||
            configuredMin > configuredMax)
            return null;

        var exact = DeserializeBounds(config.TileBoundsJson);
        if (exact.Count > 0)
        {
            // A provider-supplied bounds table is authoritative. In particular,
            // do not synthesize requests for declared zooms missing from it:
            // MapGenie may over-zoom the last native level in its web client.
            var supported = exact
                .Where(x => x.Key >= configuredMin && x.Key <= configuredMax)
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.Value);
            return supported.Count == 0 ? null : new TileCoverage(supported);
        }

        var calculated = Enumerable.Range(configuredMin, configuredMax - configuredMin + 1)
            .ToDictionary(
                zoom => zoom,
                zoom => FromGeographicBounds(config.MinLatitude, config.MaxLatitude,
                    config.MinLongitude, config.MaxLongitude, zoom));
        return new TileCoverage(calculated);
    }

    private static Dictionary<int, TileRange> DeserializeBounds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, TileRange>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static TileRange FromGeographicBounds(
        double minLatitude, double maxLatitude, double minLongitude, double maxLongitude, int zoom)
    {
        var a = ToTile(minLatitude, minLongitude, zoom);
        var b = ToTile(maxLatitude, maxLongitude, zoom);
        return new TileRange(
            Math.Min(a.X, b.X), Math.Max(a.X, b.X),
            Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
    }

    private static (int X, int Y) ToTile(double latitude, double longitude, int zoom)
    {
        latitude = Math.Clamp(latitude, -85.05112878, 85.05112878);
        var scale = 1 << zoom;
        var x = (int)Math.Floor((longitude + 180) / 360 * scale);
        var radians = latitude * Math.PI / 180;
        var y = (int)Math.Floor((1 - Math.Log(Math.Tan(radians) + 1 / Math.Cos(radians)) / Math.PI) / 2 * scale);
        return (Math.Clamp(x, 0, scale - 1), Math.Clamp(y, 0, scale - 1));
    }
}

internal static class TilePlaceholder
{
    // A valid transparent 1x1 PNG. Persisting it prevents known unavailable
    // coordinates from being requested again by either downloader or renderer.
    public static readonly byte[] TransparentPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+Xw4AAAAASUVORK5CYII=");
}
