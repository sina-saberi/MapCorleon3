using System.Net;
using System.Net.WebSockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MapCorleon.Contracts;

namespace MapCorleon.MapGenie;

public sealed class MapGenieProvider(IMapImportSink sink, IProviderStorage storage, HttpClient http) : IMapProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public string Key => "mapgenie";
    public string DisplayName => "MapGenie";

    public async Task<ImportResult> ImportAsync(MapImportRequest request, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(request.PageUrl, UriKind.Absolute, out var pageUri) || pageUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter a valid MapGenie map webpage URL.");

        progress?.Report(new("Downloading map webpage", 0, 1));
        var rendered = await RenderPageDataAsync(pageUri, progress, ct);
        var mapJson = rendered.MapData;
        var gameJson = rendered.Game;
        var source = JsonSerializer.Deserialize<MapGenieDocument>(mapJson, JsonOptions)
            ?? throw new InvalidDataException("window.mapData is empty or invalid.");
        var game = JsonSerializer.Deserialize<MapGenieGame>(gameJson, JsonOptions)
            ?? throw new InvalidDataException("window.game is empty or invalid.");

        progress?.Report(new("Downloading marker sprites", 0, 1));
        var sprites = new Dictionary<string, SpriteRect>();
        string? spriteImage = null;
        if (!string.IsNullOrWhiteSpace(game.Config.MarkerSpriteUrl))
        {
            var pngUrl = MakeAbsolute(game.Config.CdnUrl, pageUri, game.Config.MarkerSpriteUrl);
            spriteImage = (await storage.CacheAsync(Key, "sprites", pngUrl, "markers.png", ct)).FullPath;
            var jsonUrl = ReplacePathExtension(pngUrl, ".json");
            try
            {
                var spriteJson = await storage.CacheAsync(Key, "sprites", jsonUrl, "markers.json", ct);
                await using var stream = File.OpenRead(spriteJson.FullPath);
                sprites = await JsonSerializer.DeserializeAsync<Dictionary<string, SpriteRect>>(stream, JsonOptions, ct) ?? [];
            }
            catch (HttpRequestException) { }
        }

        var groups = source.Groups.Select(g => new ImportedGroup(g.Id, g.Title, true,
            g.Categories.Select(c => new ImportedCategory(c.Id, c.GroupId, c.Title, c.Visible, c.Icon,
                c.Icon is not null && sprites.TryGetValue(c.Icon, out var rect) ? rect : null)).ToList())).ToList();
        var locations = source.Locations.Select(location => new ImportedLocation(location.Id, location.CategoryId,
            location.Title, location.Description ?? string.Empty, location.Latitude, location.Longitude,
            location.Media.Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new ImportedMedia(x.Id, x.Title, x.Url!)).ToList())).ToList();
        var tile = source.Config.TileSets.OrderBy(x => x.MinZoom).FirstOrDefault();
        var pattern = tile?.Pattern is null ? null : ExpandTilePattern(tile.Pattern, game.Config.TilesBaseUrl, pageUri);
        var bounds = tile?.Bounds?
            .Where(x => int.TryParse(x.Key, out _))
            .ToDictionary(x => int.Parse(x.Key), x => new TileRange(x.Value.X.Min, x.Value.X.Max, x.Value.Y.Min, x.Value.Y.Max));
        progress?.Report(new("Saving imported map", 0, source.Locations.Count));
        return await sink.UpsertAsync(game.Title, new ImportedMap(Key, source.Map.Id, source.Map.Title,
            source.Config.StartLat, source.Config.StartLng, source.Config.InitialZoom, pattern, tile?.MinZoom,
            tile?.MaxZoom, bounds, spriteImage, groups, locations), progress, ct);
    }

    public async Task<byte[]?> DownloadTileAsync(string url, CancellationToken ct = default)
    {
        using var request = CreateRequest(url, "image/avif,image/webp,image/apng,image/*,*/*;q=0.8", "image");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<string> DownloadTextAsync(Uri uri, CancellationToken ct)
    {
        using var request = CreateRequest(uri.ToString(), "text/html,application/xhtml+xml,*/*;q=0.8", "document");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static async Task<(string MapData, string Game)> RenderPageDataAsync(
        Uri pageUri, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var chrome = FindChrome();
        var profile = Path.Combine(Path.GetTempPath(), "MapCorleon", "chrome", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = chrome,
            Arguments = $"--headless=new --disable-gpu --no-first-run --no-default-browser-check --remote-debugging-port=0 --user-data-dir=\"{profile}\" \"{pageUri}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Chrome could not be started for webpage scraping.");
        try
        {
            var portFile = Path.Combine(profile, "DevToolsActivePort");
            for (var i = 0; i < 100 && !File.Exists(portFile); i++) await Task.Delay(100, ct);
            if (!File.Exists(portFile)) throw new TimeoutException("Chrome did not expose its page debugging endpoint.");
            var lines = await File.ReadAllLinesAsync(portFile, ct);
            using var local = new HttpClient();
            var targets = await local.GetStringAsync($"http://127.0.0.1:{lines[0]}/json/list", ct);
            using var targetList = JsonDocument.Parse(targets);
            var pageTarget = targetList.RootElement.EnumerateArray()
                .FirstOrDefault(x => x.TryGetProperty("type", out var type) && type.GetString() == "page");
            if (pageTarget.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("Chrome did not create a webpage target.");
            var socketUri = new Uri(pageTarget.GetProperty("webSocketDebuggerUrl").GetString()!);
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(socketUri, ct);
            for (var attempt = 0; attempt < 120; attempt++)
            {
                progress?.Report(new("Waiting for webpage locations", attempt, 120));
                var result = await EvaluateAsync(socket,
                    "JSON.stringify({mapData:window.mapData,game:window.game,ready:!!(window.mapData&&window.mapData.locations&&window.mapData.locations.length)})",
                    attempt + 1, ct);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    using var payload = JsonDocument.Parse(result);
                    if (payload.RootElement.GetProperty("ready").GetBoolean())
                        return (payload.RootElement.GetProperty("mapData").GetRawText(), payload.RootElement.GetProperty("game").GetRawText());
                }
                await Task.Delay(250, ct);
            }
            throw new TimeoutException("The webpage did not finish loading its locations.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
            try { Directory.Delete(profile, true); } catch { }
        }
    }

    private static async Task<string?> EvaluateAsync(ClientWebSocket socket, string expression, int id, CancellationToken ct)
    {
        var command = JsonSerializer.Serialize(new { id, method = "Runtime.evaluate", @params = new { expression, returnByValue = true } });
        await socket.SendAsync(Encoding.UTF8.GetBytes(command), WebSocketMessageType.Text, true, ct);
        var buffer = new byte[1024 * 1024];
        while (true)
        {
            using var output = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer, ct);
                output.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);
            using var response = JsonDocument.Parse(output.ToArray());
            if (!response.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
            if (!response.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("result", out var value) ||
                !value.TryGetProperty("value", out var actual)) return null;
            return actual.GetString();
        }
    }

    private static string FindChrome()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Chrome or Microsoft Edge is required to scrape JavaScript-rendered map pages.");
    }

    private static string ExtractAssignedJson(string html, string variable)
    {
        var assignment = -1;
        var searchFrom = 0;
        while (true)
        {
            var candidate = html.IndexOf(variable, searchFrom, StringComparison.Ordinal);
            if (candidate < 0) break;
            var equals = candidate + variable.Length;
            while (equals < html.Length && char.IsWhiteSpace(html[equals])) equals++;
            if (equals < html.Length && html[equals] == '=') { assignment = equals; break; }
            searchFrom = candidate + variable.Length;
        }
        if (assignment < 0) throw new InvalidDataException($"{variable} assignment was not found on this page.");
        var start = html.IndexOfAny(['{', '['], assignment + 1);
        if (start < 0) throw new InvalidDataException($"{variable} has no JSON value.");
        var stack = new Stack<char>();
        var inString = false; var escaped = false;
        for (var i = start; i < html.Length; i++)
        {
            var ch = html[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }
            if (ch == '"') { inString = true; continue; }
            if (ch is '{' or '[') stack.Push(ch);
            else if (ch is '}' or ']')
            {
                if (stack.Count == 0) break;
                var open = stack.Pop();
                if ((open == '{' && ch != '}') || (open == '[' && ch != ']')) throw new InvalidDataException($"{variable} contains malformed JSON.");
                if (stack.Count == 0) return html[start..(i + 1)];
            }
        }
        throw new InvalidDataException($"{variable} JSON was not terminated.");
    }

    private static string MakeAbsolute(string? baseUrl, Uri pageUri, string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)) return absolute.ToString();
        var origin = Uri.TryCreate(baseUrl, UriKind.Absolute, out var configured) ? configured : new Uri(pageUri.GetLeftPart(UriPartial.Authority));
        return new Uri(origin, value).ToString();
    }
    private static string ReplacePathExtension(string url, string extension)
    {
        var builder = new UriBuilder(url) { Path = Path.ChangeExtension(new Uri(url).AbsolutePath, extension) };
        return builder.Uri.ToString();
    }
    private static string ExpandTilePattern(string pattern, string? tilesBaseUrl, Uri pageUri)
    {
        if (pattern.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return pattern;
        var baseUrl = string.IsNullOrWhiteSpace(tilesBaseUrl) ? pageUri.GetLeftPart(UriPartial.Authority) : tilesBaseUrl;
        return $"{baseUrl.TrimEnd('/')}/games/{pattern.TrimStart('/')}";
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
