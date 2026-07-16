using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
using System.IO;
using MapCorleon.Contracts;

namespace MapCorleon.Services;

public sealed class ProviderStorage(ISettingsService settings, HttpClient http) : IProviderStorage
{
    public async Task<StoredAsset> CacheAsync(string provider, string kind, string source, string? preferredFileName = null, CancellationToken ct = default)
    {
        if (!IsRemote(source))
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Provider asset was not found.", source);
            return await CacheFileAsync(provider, kind, source, preferredFileName, ct);
        }

        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..24];
        var extension = GetExtension(preferredFileName) ?? GetExtension(new Uri(source).AbsolutePath) ?? ".bin";
        var directory = Path.Combine(settings.Current.StorageDirectory, "providers", provider, kind);
        var path = Path.Combine(directory, sourceHash + extension);
        if (File.Exists(path)) return new(path, await HashFileAsync(path, ct));

        using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(directory);
        var temporary = path + ".download";
        await using (var output = File.Create(temporary))
            await response.Content.CopyToAsync(output, ct);
        File.Move(temporary, path, true);
        return new(path, await HashFileAsync(path, ct));
    }

    private async Task<StoredAsset> CacheFileAsync(string provider, string kind, string source, string? preferredFileName, CancellationToken ct)
    {
        var hash = await HashFileAsync(source, ct);
        var extension = GetExtension(preferredFileName) ?? GetExtension(source) ?? ".bin";
        var directory = Path.Combine(settings.Current.StorageDirectory, "providers", provider, kind);
        var path = Path.Combine(directory, hash[..24] + extension);
        Directory.CreateDirectory(directory);
        if (!File.Exists(path)) File.Copy(source, path);
        return new(path, hash);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
    private static string? GetExtension(string? value)
    {
        var extension = Path.GetExtension(value);
        return string.IsNullOrWhiteSpace(extension) || extension.Length > 10 ? null : extension.ToLowerInvariant();
    }
    private static bool IsRemote(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
