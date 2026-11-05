using System.IO;
using System.Text.Json;
using MapCorleon.Models;

namespace MapCorleon.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task SaveAsync(AppSettings settings);
}

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MapCorleon",
        "settings.json");

    public JsonSettingsService()
    {
        Current = Load();
        EnsureDirectories(Current);
    }

    public AppSettings Current { get; private set; }

    public async Task SaveAsync(AppSettings settings)
    {
        settings = Normalize(settings);
        EnsureDirectories(settings);
        Current = settings;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private AppSettings Load()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.TryGetProperty(nameof(AppSettings.DataDirectory), out _))
                return Normalize(JsonSerializer.Deserialize<AppSettings>(root.GetRawText()) ?? AppSettings.Default);

            // Migrate the former three-directory settings. DatabaseDirectory is the
            // closest equivalent to the new common data root.
            var legacyRoot = root.TryGetProperty("DatabaseDirectory", out var database)
                ? database.GetString()
                : null;
            var filter = root.TryGetProperty("Filter", out var filterValue)
                ? (MarkFilter)filterValue.GetInt32()
                : MarkFilter.All;
            var visibility = root.TryGetProperty("CategoryVisibility", out var categories)
                ? JsonSerializer.Deserialize<Dictionary<int, bool>>(categories.GetRawText()) ?? []
                : [];
            var map = root.TryGetProperty("CurrentMapName", out var mapValue) ? mapValue.GetString() : null;
            int? timeline = root.TryGetProperty("CurrentTimelineId", out var timelineValue) &&
                            timelineValue.ValueKind == JsonValueKind.Number
                ? timelineValue.GetInt32()
                : null;
            return Normalize(new AppSettings(
                string.IsNullOrWhiteSpace(legacyRoot) ? AppSettings.Default.DataDirectory : legacyRoot,
                filter, visibility, map, timeline));
        }
        catch { return AppSettings.Default; }
    }

    private static void EnsureDirectories(AppSettings settings)
    {
        Directory.CreateDirectory(settings.DataDirectory);
        Directory.CreateDirectory(settings.StorageDirectory);
        Directory.CreateDirectory(settings.TileDirectory);
    }

    private static AppSettings Normalize(AppSettings settings) => settings with
    {
        DataDirectory = Path.GetFullPath(settings.DataDirectory.Trim()),
        GameOverlays = settings.GameOverlays ?? new(StringComparer.Ordinal)
    };
}
