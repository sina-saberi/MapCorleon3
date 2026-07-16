using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using MapCorleon.Contracts;
using MapCorleon.Data;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Services;

public interface IImportTaskService : INotifyPropertyChanged
{
    bool IsRunning { get; }
    string Status { get; }
    int Percentage { get; }
    bool TryStart(IMapProvider provider, ImportResult result, bool downloadMedia, bool downloadTiles);
}

public sealed class ImportTaskService(
    IDbContextFactory<AppDbContext> factory,
    IProviderStorage storage,
    ISettingsService settings) : IImportTaskService
{
    private bool isRunning;
    private string status = string.Empty;
    private int percentage;
    public bool IsRunning { get => isRunning; private set => Set(ref isRunning, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public int Percentage { get => percentage; private set => Set(ref percentage, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool TryStart(IMapProvider provider, ImportResult result, bool downloadMedia, bool downloadTiles)
    {
        if (IsRunning || (!downloadMedia && !downloadTiles)) return false;
        Status = downloadMedia && downloadTiles ? "Preparing offline media and tiles"
            : downloadMedia ? "Preparing offline media"
            : "Preparing offline tiles";
        Percentage = 0;
        IsRunning = true;
        _ = RunAsync(provider, result, downloadMedia, downloadTiles);
        return true;
    }

    private async Task RunAsync(IMapProvider provider, ImportResult result, bool downloadMedia, bool downloadTiles)
    {
        try
        {
            if (downloadMedia) await DownloadMediaAsync(provider.Key, result.MapExternalId);
            if (downloadTiles) await DownloadTilesAsync(provider, result.MapExternalId);
            Status = "Offline download complete"; Percentage = 100;
            await Task.Delay(2500);
        }
        catch (Exception ex)
        {
            Status = $"Offline download failed: {ex.Message}";
            await Task.Delay(5000);
        }
        finally { IsRunning = false; Status = string.Empty; Percentage = 0; }
    }

    private async Task DownloadMediaAsync(string provider, long mapExternalId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var media = await db.Media
            .Where(x => x.Location!.Category!.Group!.Map!.ExternalId == mapExternalId && x.RelativePath.StartsWith("http"))
            .ToListAsync();
        for (var i = 0; i < media.Count; i++)
        {
            Status = $"Downloading media {i + 1} of {media.Count}";
            Percentage = media.Count == 0 ? 100 : (int)Math.Round((i + 1) * 100d / media.Count);
            var item = media[i];
            var asset = await storage.CacheAsync(provider, "media", item.RelativePath, Path.GetFileName(new Uri(item.RelativePath).AbsolutePath));
            item.RelativePath = asset.FullPath; item.ContentHash = asset.ContentHash;
            await db.SaveChangesAsync();
        }
    }

    private async Task DownloadTilesAsync(IMapProvider provider, long mapExternalId)
    {
        Status = "Reading tile download plan";
        await using var db = await factory.CreateDbContextAsync();
        var config = await db.MapConfigs.AsNoTracking().FirstAsync(x => x.Map!.ExternalId == mapExternalId);
        if (string.IsNullOrWhiteSpace(config.TilePattern) || config.MinZoom is null || config.MaxZoom is null) return;
        var exactBounds = string.IsNullOrWhiteSpace(config.TileBoundsJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<int, TileRange>>(config.TileBoundsJson);
        var ranges = Enumerable.Range(config.MinZoom.Value, config.MaxZoom.Value - config.MinZoom.Value + 1)
            .Select(z => (Zoom: z, Range: exactBounds is not null && exactBounds.TryGetValue(z, out var bound)
                ? (bound.MinX, bound.MaxX, bound.MinY, bound.MaxY)
                : GetRange(config.MinLatitude, config.MaxLatitude, config.MinLongitude, config.MaxLongitude, z)))
            .ToList();
        var total = ranges.Sum(x => (x.Range.MaxX - x.Range.MinX + 1) * (x.Range.MaxY - x.Range.MinY + 1));
        var done = 0;
        var failed = 0;
        var cacheName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(config.TilePattern)))[..16];
        Status = $"Downloading tiles 0 of {total}";
        foreach (var (zoom, range) in ranges)
        foreach (var x in Enumerable.Range(range.MinX, range.MaxX - range.MinX + 1))
        foreach (var y in Enumerable.Range(range.MinY, range.MaxY - range.MinY + 1))
        {
            var path = Path.Combine(settings.Current.TileDirectory, "mapsui", cacheName, zoom.ToString(), x.ToString(), $"{y}.tile");
            if (!File.Exists(path))
            {
                var url = config.TilePattern.Replace("{z}", zoom.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
                try
                {
                    var bytes = await provider.DownloadTileAsync(url);
                    if (bytes is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        await File.WriteAllBytesAsync(path, bytes);
                    }
                    else failed++;
                }
                catch (HttpRequestException) { failed++; }
            }
            Status = $"Downloading tiles {++done} of {total}";
            Percentage = total == 0 ? 100 : (int)Math.Round(done * 100d / total);
        }
        if (failed > 0) throw new InvalidOperationException($"{failed} of {total} tiles could not be downloaded.");
    }

    private static (int MinX, int MaxX, int MinY, int MaxY) GetRange(double minLat, double maxLat, double minLon, double maxLon, int zoom)
    {
        var a = ToTile(minLat, minLon, zoom); var b = ToTile(maxLat, maxLon, zoom);
        return (Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
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
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
