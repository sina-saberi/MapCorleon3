using System.IO;

namespace MapCorleon.Models;

public sealed record AppSettings(
    string TileDirectory,
    string StorageDirectory,
    string DatabaseDirectory,
    MarkFilter Filter,
    Dictionary<int, bool> CategoryVisibility,
    TimeSpan TileMaxAge,
    string? CurrentMapName = null,
    int? CurrentTimelineId = null)
{
    public static AppSettings Default
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MapCorleon");

            return new AppSettings(
                TileDirectory: Path.Combine(root, "tiles"),
                StorageDirectory: Path.Combine(root, "storage"),
                DatabaseDirectory: root,
                Filter: MarkFilter.All,
                CategoryVisibility: [],
                TileMaxAge: TimeSpan.FromDays(30));
        }
    }
}
