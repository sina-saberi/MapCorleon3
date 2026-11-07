using System.Net.WebSockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MapCorleon.Contracts;

namespace MapCorleon.MapGenie;

internal sealed class MapGenieExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public async Task<(string Game, ImportedMap Map, BrowserAssets Assets)> ReadAsync(
        Uri pageUri, string? iconStylesheet, bool listOnly, CancellationToken ct)
    {
        var rendered = await RenderPageDataAsync(pageUri, iconStylesheet, listOnly, ct);
        var mapJson = rendered.MapData;
        var gameJson = rendered.Game;
        var source = JsonSerializer.Deserialize<MapGenieDocument>(mapJson, JsonOptions)
            ?? throw new InvalidDataException("window.mapData is empty or invalid.");
        var game = JsonSerializer.Deserialize<MapGenieGame>(gameJson, JsonOptions)
            ?? throw new InvalidDataException("window.game is empty or invalid.");

        var groups = source.Groups.Select(g => new ImportedGroup(g.Id, g.Title, true,
            g.Categories.Select(c => new ImportedCategory(c.Id, c.GroupId, c.Title, c.Visible, c.Icon, null)).ToList())).ToList();
        var locations = source.Locations.Select(location => new ImportedLocation(location.Id, location.CategoryId,
            location.Title, location.Description ?? string.Empty, location.Latitude, location.Longitude,
            location.Media.Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new ImportedMedia(x.Id, x.Title, x.Url!)).ToList())).ToList();
        var tile = source.Config.TileSets.OrderBy(x => x.MinZoom).FirstOrDefault();
        var pattern = tile?.Pattern is null ? null : ExpandTilePattern(tile.Pattern, game.Config.TilesBaseUrl, pageUri);
        var bounds = tile?.Bounds?
            .Where(x => int.TryParse(x.Key, out _))
            .ToDictionary(x => int.Parse(x.Key), x => new TileRange(x.Value.X.Min, x.Value.X.Max, x.Value.Y.Min, x.Value.Y.Max));
        if (source.Map.Id <= 0 || locations.Count == 0 || groups.Count == 0)
            throw new InvalidDataException("The page did not contain a usable map.");
        return (game.Title, new ImportedMap("mapgenie", source.Map.Id, source.Map.Title,
            source.Config.StartLat, source.Config.StartLng, source.Config.InitialZoom, pattern, tile?.MinZoom,
            tile?.MaxZoom, bounds, null, groups, locations), rendered.Assets);
    }

    private static async Task<(string MapData, string Game, BrowserAssets Assets)> RenderPageDataAsync(
        Uri pageUri, string? iconStylesheet, bool listOnly, CancellationToken ct)
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
                var result = await EvaluateAsync(socket,
                    "JSON.stringify({mapData:window.mapData,game:window.game,ready:!!(window.mapData&&window.mapData.locations&&window.mapData.locations.length)})",
                    attempt + 1, ct);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    using var payload = JsonDocument.Parse(result);
                    if (payload.RootElement.GetProperty("ready").GetBoolean() && payload.RootElement.TryGetProperty("game", out var game))
                    {
                        var assetsJson = await EvaluateAsync(socket, """
                            (async (overrideCss, listOnly) => {
                                const links = [...new Set([...document.querySelectorAll('a[href]')].map(a => a.href))];
                                if (listOnly) return JSON.stringify({links});
                                const styles = [...document.querySelectorAll('link[href]')].map(l => l.href);
                                const slug = window.mapData.mapConfig?.tile_sets?.[0]?.pattern?.match(/(?:^|\/games\/)([^/]+)\//)?.[1]
                                    ?? location.pathname.split('/').filter(Boolean)[0];
                                const urls = [overrideCss, ...styles.filter(h => h.includes('/game-icons/') && h.includes('.css')),
                                    slug && new URL('/game-icons/' + slug + '/icons.css', location.origin).href].filter(Boolean);
                                async function base64(response) {
                                    const bytes = new Uint8Array(await response.arrayBuffer());
                                    return btoa(Array.from(bytes, byte => String.fromCharCode(byte)).join(''));
                                }
                                for (const url of new Set(urls)) {
                                    try {
                                        const response = await fetch(url, {signal: AbortSignal.timeout(10000)});
                                        if (!response.ok) continue;
                                        const css = await response.text();
                                        const fonts = [...css.matchAll(/url\(\s*['"]?([^'")\s]+)/g)]
                                            .map(match => new URL(match[1], response.url));
                                        const font = fonts.find(u => /\.(ttf|otf)$/i.test(u.pathname))
                                            ?? fonts.find(u => /\.woff$/i.test(u.pathname));
                                        if (!font) continue;
                                        const downloaded = await fetch(font, {signal: AbortSignal.timeout(10000)});
                                        if (!downloaded.ok) continue;
                                        return JSON.stringify({links, css, font: await base64(downloaded)});
                                    } catch {}
                                }
                                const config = window.game?.config;
                                if (config?.marker_sprite_url) {
                                    try {
                                        const url = new URL(config.marker_sprite_url, config.cdn_url || location.origin);
                                        const metadata = new URL(url);
                                        metadata.pathname = metadata.pathname.replace(/\.[^.]+$/, '.json');
                                        const png = await fetch(url, {signal: AbortSignal.timeout(10000)});
                                        const json = await fetch(metadata, {signal: AbortSignal.timeout(10000)});
                                        if (png.ok && json.ok)
                                            return JSON.stringify({links, sprite: await base64(png), spriteMetadata: await json.text()});
                                    } catch {}
                                }
                                return JSON.stringify({links});
                            })
                            """ + $"({JsonSerializer.Serialize(iconStylesheet)}, {(listOnly ? "true" : "false")})", 121, ct);
                        var assets = JsonSerializer.Deserialize<BrowserAssets>(assetsJson ?? "{}", JsonOptions)!;
                        return (payload.RootElement.GetProperty("mapData").GetRawText(), game.GetRawText(), assets);
                    }
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
        var command = JsonSerializer.Serialize(new { id, method = "Runtime.evaluate", @params = new { expression, returnByValue = true, awaitPromise = true } });
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

    private static string ExpandTilePattern(string pattern, string? tilesBaseUrl, Uri pageUri)
    {
        if (pattern.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return pattern;
        var baseUrl = string.IsNullOrWhiteSpace(tilesBaseUrl) ? pageUri.GetLeftPart(UriPartial.Authority) : tilesBaseUrl;
        return $"{baseUrl.TrimEnd('/')}/games/{pattern.TrimStart('/')}";
    }
}

internal sealed record BrowserAssets(string[]? Links, string? Css, string? Font, string? Sprite, string? SpriteMetadata);
