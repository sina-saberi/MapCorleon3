using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MapCorleon.Services;

public interface IMediaPathResolver
{
    string Resolve(string path);
}

public sealed class MediaPathResolver(ISettingsService settings) : IMediaPathResolver
{
    public string Resolve(string path)
    {
        if (File.Exists(path) || !IsRemote(path))
            return path;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..24];
        var directory = Path.Combine(
            settings.Current.StorageDirectory,
            "providers",
            "mapgenie",
            "media");

        if (!Directory.Exists(directory))
            return path;

        var extension = GetExtension(new Uri(path).AbsolutePath);
        if (extension is not null)
        {
            var expectedPath = Path.Combine(directory, hash + extension);
            if (File.Exists(expectedPath))
                return expectedPath;
        }

        // Older downloads may have used a content-derived or fallback extension.
        return Directory.EnumerateFiles(directory, hash + ".*").FirstOrDefault() ?? path;
    }

    private static string? GetExtension(string value)
    {
        var extension = Path.GetExtension(value);
        return string.IsNullOrWhiteSpace(extension) || extension.Length > 10
            ? null
            : extension.ToLowerInvariant();
    }

    private static bool IsRemote(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";
}
