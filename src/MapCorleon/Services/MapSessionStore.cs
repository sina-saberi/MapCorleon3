using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MapCorleon.Services;

internal sealed record MapViewportState(double X, double Y, double Resolution);
internal sealed record WindowPlacementState(int Left, int Top, int Right, int Bottom, bool Maximized);

internal sealed record MapSessionState
{
    public MapViewportState? Viewport { get; init; }
    public WindowPlacementState? Window { get; init; }
    public int? LocationId { get; init; }
    public int? TimelineId { get; init; }
    public HashSet<int> CollapsedGroups { get; init; } = [];
}

public sealed class MapSessionStore
{
    private readonly string path;
    private readonly ILogger<MapSessionStore> logger;
    private readonly Dictionary<string, MapSessionState> sessions;
    private static readonly MapSessionState Empty = new();
    private bool hasUnsavedChanges;

    public MapSessionStore(ILogger<MapSessionStore> logger)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MapCorleon", "map-sessions.json"), logger)
    {
    }

    internal MapSessionStore(string path, ILogger<MapSessionStore> logger)
    {
        this.path = path;
        this.logger = logger;
        sessions = Load();
    }

    internal MapSessionState Read(string? key) =>
        key is not null && sessions.TryGetValue(key, out var state) ? state : Empty;

    internal void Update(string? key, Func<MapSessionState, MapSessionState> update)
    {
        if (key is null) return;
        var previous = Read(key);
        var next = update(previous);
        if (previous != next)
        {
            sessions[key] = next;
            hasUnsavedChanges = true;
        }
        if (!hasUnsavedChanges) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(sessions));
            File.Move(temporary, path, overwrite: true);
            hasUnsavedChanges = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save MapCorleon map sessions.");
        }
    }

    private Dictionary<string, MapSessionState> Load()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, MapSessionState?>>(File.ReadAllText(path)) ?? [];
            return saved.Where(entry => entry.Value is not null).ToDictionary(
                entry => entry.Key,
                entry => entry.Value! with { CollapsedGroups = entry.Value!.CollapsedGroups ?? [] });
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Could not load MapCorleon map sessions.");
            return [];
        }
    }
}
