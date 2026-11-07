using System.Net;
using MapCorleon.Contracts;

namespace MapCorleon.MapGenie;

public sealed class MapGenieProvider(HttpClient http) : IMapProvider
{
    public string Key => "mapgenie";
    public string DisplayName => "MapGenie";

    public async Task<byte[]?> DownloadTileAsync(string url, CancellationToken ct = default)
    {
        using var request = CreateRequest(url, "image/avif,image/webp,image/apng,image/*,*/*;q=0.8", "image");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            // MapGenie's S3 tile storage returns AccessDenied for coordinates
            // inside its rectangular bounds where no tile object exists.
            var error = await response.Content.ReadAsStringAsync(ct);
            if (error.Contains("<Code>AccessDenied</Code>", StringComparison.OrdinalIgnoreCase))
                return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static HttpRequestMessage CreateRequest(string url, string accept, string destination)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("MapCorleon/1.0 (desktop map client)");
        request.Headers.Referrer = new Uri("https://mapgenie.io/");
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", destination);
        return request;
    }
}
