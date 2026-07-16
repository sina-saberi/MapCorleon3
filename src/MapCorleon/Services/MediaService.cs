using System.IO;
using System.Security.Cryptography;
using MapCorleon.Models;

namespace MapCorleon.Services;

public interface IMediaService
{
    Task<Media> StoreAsync(int locationId, string file, CancellationToken ct = default);
}

public sealed class MediaService(ISettingsService settings) : IMediaService
{
    public async Task<Media> StoreAsync(int locationId, string file, CancellationToken ct = default)
    {
        await using var input = File.OpenRead(file);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
        var extension = Path.GetExtension(file).ToLowerInvariant();

        var blobDirectory = Path.Combine(settings.Current.StorageDirectory, "blobs");
        Directory.CreateDirectory(blobDirectory);
        var blobPath = Path.Combine(blobDirectory, hash + extension);
        if (!File.Exists(blobPath)) File.Copy(file, blobPath);

        var locationDirectory = Path.Combine(settings.Current.StorageDirectory, "locations", locationId.ToString());
        Directory.CreateDirectory(locationDirectory);

        var locationPath = Path.Combine(locationDirectory, hash + extension);
        if (!File.Exists(locationPath)) File.Copy(blobPath, locationPath);

        return new Media
        {
            LocationId = locationId,
            ContentHash = hash,
            RelativePath = locationPath
        };
    }
}
