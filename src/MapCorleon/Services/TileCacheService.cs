using System.IO;

namespace MapCorleon.Services;

public interface ITileCacheService
{
    Task<byte[]?> GetAsync(
        string provider,
        int zoom,
        int x,
        int y,
        Func<CancellationToken, Task<byte[]?>> remote,
        CancellationToken ct = default);

    Task ClearExpiredAsync(CancellationToken ct = default);
}

public sealed class TileCacheService(ISettingsService settings) : ITileCacheService
{
    public async Task<byte[]?> GetAsync(
        string provider,
        int zoom,
        int x,
        int y,
        Func<CancellationToken, Task<byte[]?>> remote,
        CancellationToken ct = default)
    {
        var path = Path.Combine(
            settings.Current.TileDirectory,
            provider,
            zoom.ToString(),
            x.ToString(),
            $"{y}.tile");

        if (File.Exists(path) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < settings.Current.TileMaxAge)
        {
            return await File.ReadAllBytesAsync(path, ct);
        }

        var data = await remote(ct);
        if (data is null)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, data, ct);
        return data;
    }

    public Task ClearExpiredAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(settings.Current.TileDirectory))
            return Task.CompletedTask;

        var expiredFiles = Directory
            .EnumerateFiles(settings.Current.TileDirectory, "*.tile", SearchOption.AllDirectories)
            .Where(path =>
                DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= settings.Current.TileMaxAge);

        foreach (var path in expiredFiles)
            File.Delete(path);

        return Task.CompletedTask;
    }
}
