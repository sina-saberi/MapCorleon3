using System.Text.Json;
namespace MapCorleon.Import;

public sealed class MapGenieImportProvider(IMapImportSink sink, HttpClient http) : IMapImportProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public string Name => "MapGenie";

    public async Task<ImportResult> ImportAsync(MapImportRequest request, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.GameName)) throw new ArgumentException("Game name is required.");
        if (!File.Exists(request.MapJsonPath)) throw new FileNotFoundException("Map JSON was not found.", request.MapJsonPath);
        if (!IsRemote(request.SpriteJsonPath) && !File.Exists(request.SpriteJsonPath)) throw new FileNotFoundException("Sprite JSON was not found.", request.SpriteJsonPath);
        progress?.Report(new("Reading MapGenie files", 0, 1));

        await using var mapStream = File.OpenRead(request.MapJsonPath);
        var source = await JsonSerializer.DeserializeAsync<MapGenieDocument>(mapStream, JsonOptions, ct)
            ?? throw new InvalidDataException("The map JSON is empty or invalid.");
        await using var spriteStream = await OpenSourceAsync(request.SpriteJsonPath, ct);
        var sprites = await JsonSerializer.DeserializeAsync<Dictionary<string, SpriteRect>>(spriteStream, JsonOptions, ct) ?? [];
        var tile = source.Config.TileSets.OrderBy(x => x.MinZoom).FirstOrDefault();
        var spriteImage = DeriveSpriteImageLocation(request.SpriteJsonPath);

        var groups = source.Groups.Select(g => new ImportedGroup(g.Id, g.Title, true,
            g.Categories.Select(c => new ImportedCategory(c.Id, c.GroupId, c.Title, c.Visible, c.Icon,
                c.Icon is not null && sprites.TryGetValue(c.Icon, out var rect) ? rect : null)).ToList())).ToList();
        var locations = source.Locations.Select(l => new ImportedLocation(l.Id, l.CategoryId, l.Title,
            l.Description ?? string.Empty, l.Latitude, l.Longitude,
            l.Media.Where(m => !string.IsNullOrWhiteSpace(m.Url)).Select(m => new ImportedMedia(m.Id, m.Title, m.Url!, m.FileName, m.MimeType)).ToList())).ToList();
        var map = new ImportedMap(source.Map.Id, source.Map.Title, source.Config.StartLat, source.Config.StartLng,
            source.Config.InitialZoom, tile?.Pattern, tile?.MinZoom, tile?.MaxZoom, spriteImage, groups, locations);
        progress?.Report(new("Mapped MapGenie data", 1, 1));
        return await sink.UpsertAsync(request.GameName.Trim(), map, progress, ct);
    }

    private static string? DeriveSpriteImageLocation(string jsonPath)
    {
        if (Uri.TryCreate(jsonPath, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var builder = new UriBuilder(uri) { Path = Path.ChangeExtension(uri.AbsolutePath, ".png") };
            return builder.Uri.ToString();
        }
        var png = Path.ChangeExtension(jsonPath, ".png"); return File.Exists(png) ? png : null;
    }

    private async Task<Stream> OpenSourceAsync(string location, CancellationToken ct)
    {
        if (!IsRemote(location)) return File.OpenRead(location);
        var response = await SendBrowserNavigationAsync(location, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && Uri.TryCreate(location, UriKind.Absolute, out var original) && !string.IsNullOrEmpty(original.Query))
        {
            response.Dispose();
            var withoutQuery = new UriBuilder(original) { Query = string.Empty }.Uri.ToString();
            response = await SendBrowserNavigationAsync(withoutQuery, ct);
        }
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            var status = response.StatusCode; var reason = response.ReasonPhrase;
            response.Dispose();
            if (detail.Length > 300) detail = detail[..300];
            throw new HttpRequestException($"MapGenie returned {(int)status} ({reason}). The system proxy was enabled and a query-free retry was attempted. {detail}", null, status);
        }
        return new ResponseStream(await response.Content.ReadAsStreamAsync(ct), response);
    }

    private async Task<HttpResponseMessage> SendBrowserNavigationAsync(string location, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.TryAddWithoutValidation("DNT", "1");
        request.Headers.TryAddWithoutValidation("Sec-CH-UA", "\"Not;A=Brand\";v=\"8\", \"Chromium\";v=\"150\", \"Google Chrome\";v=\"150\"");
        request.Headers.TryAddWithoutValidation("Sec-CH-UA-Mobile", "?0");
        request.Headers.TryAddWithoutValidation("Sec-CH-UA-Platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    private static bool IsRemote(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false; public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count); public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); response.Dispose(); } base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); response.Dispose(); GC.SuppressFinalize(this); }
    }
}
