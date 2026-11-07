using System.IO;
using MapCorleon.Contracts;
using MapCorleon.Data;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Services;

public interface IMediaDownloadService
{
    Task<string> EnsureDownloadedAsync(int mediaId, CancellationToken ct = default);
    Task<string> EnsureDownloadedAsync(int mediaId, string provider, string storageKind, string? referrer, CancellationToken ct);
}

public sealed class MediaDownloadService(
    IDbContextFactory<AppDbContext> factory,
    IProviderStorage storage) : IMediaDownloadService
{
    public async Task<string> EnsureDownloadedAsync(int mediaId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var context = await db.Media.AsNoTracking()
            .Where(x => x.Id == mediaId)
            .Select(x => new
            {
                MapId = x.Location!.Category!.Group!.MapId,
                GameId = x.Location.Category.Group.Map!.GameId,
                Provider = x.Location.Category.Group.Map.Config!.Provider,
                x.Location.Category.Group.Map.Config!.SourcePageUrl
            })
            .SingleAsync(ct);
        var referrer = context.SourcePageUrl ?? await db.DownloadTasks.AsNoTracking()
            .Where(x => x.MapId == context.MapId && x.SourcePageUrl != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.SourcePageUrl)
            .FirstOrDefaultAsync(ct);
        var kind = Path.Combine("media", $"game-{context.GameId}", $"map-{context.MapId}");
        return await EnsureDownloadedAsync(mediaId, context.Provider, kind, referrer, ct);
    }

    public async Task<string> EnsureDownloadedAsync(int mediaId, string provider, string storageKind, string? referrer, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var media = await db.Media.SingleAsync(x => x.Id == mediaId, ct);

        // A verified final file is already a successful download. Never fetch it again.
        if (!IsRemote(media.RelativePath) && File.Exists(media.RelativePath)) return media.RelativePath;

        var source = IsRemote(media.SourceUrl) ? media.SourceUrl
            : IsRemote(media.RelativePath) ? media.RelativePath
            : null;
        if (source is null)
            throw new InvalidOperationException(
                $"The local file is missing and media #{media.Id} has no saved source URL.");

        var asset = await storage.CacheAsync(provider, storageKind, source,
            Path.GetFileName(new Uri(source).AbsolutePath), ct, referrer);
        if (!File.Exists(asset.FullPath))
            throw new IOException($"The download completed without creating the final file: {asset.FullPath}");

        // Commit the local path only after the final file has been verified.
        media.SourceUrl = source;
        media.RelativePath = asset.FullPath;
        media.ContentHash = asset.ContentHash;
        await db.SaveChangesAsync(ct);
        return asset.FullPath;
    }

    private static bool IsRemote(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
