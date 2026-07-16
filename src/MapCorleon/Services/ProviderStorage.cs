using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
using System.Net;
using System.IO;
using System.Diagnostics;
using MapCorleon.Contracts;
using Microsoft.Extensions.Logging;

namespace MapCorleon.Services;

public sealed class ProviderStorage(ISettingsService settings, HttpClient http, ILogger<ProviderStorage> logger) : IProviderStorage
{
    private static readonly SemaphoreSlim LogLock = new(1, 1);
    public string DiagnosticLogPath => Path.Combine(settings.Current.DataDirectory, "download-http.log");
    public async Task<StoredAsset> CacheAsync(string provider, string kind, string source, string? preferredFileName = null,
        CancellationToken ct = default, string? referrer = null)
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

        using var response = await SendMediaAsync(source, referrer, ct);
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

    private async Task<HttpResponseMessage> SendMediaAsync(string source, string? referrer, CancellationToken ct)
    {
        var effectiveReferrer = string.IsNullOrWhiteSpace(referrer) ? "https://mapgenie.io/" : referrer;
        using (var request = CreateMediaRequest(source, effectiveReferrer))
        {
            WriteRequestLog(source, effectiveReferrer, request);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            await LogResponseAsync(source, effectiveReferrer, response, ct);
            if (response.StatusCode != HttpStatusCode.Forbidden || effectiveReferrer == "https://mapgenie.io/")
                return response;
            response.Dispose();
        }

        using var fallback = CreateMediaRequest(source, "https://mapgenie.io/");
        WriteRequestLog(source, "https://mapgenie.io/", fallback);
        var fallbackResponse = await http.SendAsync(fallback, HttpCompletionOption.ResponseHeadersRead, ct);
        await LogResponseAsync(source, "https://mapgenie.io/", fallbackResponse, ct);
        return fallbackResponse;
    }

    private async Task LogResponseAsync(string source, string referrer, HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            Trace.WriteLine($"[MapCorleon download] {(int)response.StatusCode} {source}");
            return;
        }
        var output = new StringBuilder()
            .AppendLine($"[{DateTimeOffset.Now:O}] GET {source}")
            .AppendLine($"Referrer: {referrer}")
            .AppendLine($"Status: {(int)response.StatusCode} {response.ReasonPhrase}")
            .AppendLine("Request headers:");
        if (response.RequestMessage is { } request)
            foreach (var header in request.Headers)
                output.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        output.AppendLine("Response headers:");
        foreach (var header in response.Headers)
            output.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        foreach (var header in response.Content.Headers)
            output.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        var body = await response.Content.ReadAsStringAsync(ct);
        if (body.Length > 2048) body = body[..2048];
        output.AppendLine("Response body:").AppendLine(body);
        output.AppendLine(new string('-', 80));

        Trace.Write(output.ToString());
        logger.LogWarning("Media HTTP failure:\n{HttpDiagnostic}", output.ToString());

        await LogLock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(settings.Current.DataDirectory);
            await File.AppendAllTextAsync(DiagnosticLogPath, output.ToString(), ct);
        }
        finally { LogLock.Release(); }
    }

    private void WriteRequestLog(string source, string referrer, HttpRequestMessage request)
    {
        Trace.WriteLine($"[MapCorleon download] GET {source} (referrer: {referrer})");
    }

    private static HttpRequestMessage CreateMediaRequest(string source, string? referrer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, source)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
        };
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9,fa;q=0.8");
        request.Headers.CacheControl = new() { NoCache = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        if (Uri.TryCreate(referrer, UriKind.Absolute, out var page) && page.Scheme is "http" or "https")
            request.Headers.Referrer = page;
        request.Headers.TryAddWithoutValidation("DNT", "1");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "image");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Storage-Access", "active");
        request.Headers.TryAddWithoutValidation("Priority", "u=1, i");
        request.Headers.TryAddWithoutValidation("Sec-CH-UA", "\"Not;A=Brand\";v=\"8\", \"Chromium\";v=\"150\", \"Google Chrome\";v=\"150\"");
        request.Headers.TryAddWithoutValidation("Sec-CH-UA-Mobile", "?0");
        request.Headers.TryAddWithoutValidation("Sec-CH-UA-Platform", "\"Windows\"");
        return request;
    }
}
