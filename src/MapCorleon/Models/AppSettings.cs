using System.IO;
using System.Text.Json.Serialization;

namespace MapCorleon.Models;

public sealed record AppSettings(
    string DataDirectory,
    MarkFilter Filter,
    Dictionary<int, bool> CategoryVisibility,
    string? CurrentMapName = null,
    int? CurrentTimelineId = null)
{
    [JsonIgnore] public string TileDirectory => Path.Combine(DataDirectory, "tiles");
    [JsonIgnore] public string StorageDirectory => Path.Combine(DataDirectory, "storage");
    [JsonIgnore] public string DatabasePath => Path.Combine(DataDirectory, "mapcorleon.db");

    public static AppSettings Default
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MapCorleon");
            return new AppSettings(root, MarkFilter.All, []);
        }
    }
}
