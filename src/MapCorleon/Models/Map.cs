using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using MapCorleon.Services;

namespace MapCorleon.Models;

public sealed class Map : IEntity
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public long? ExternalId { get; set; }
    public required string Name { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Game?.Name)
        ? Name
        : $"{Game.Name} / {Name}";

    public Game? Game { get; set; }
    public MapConfig? Config { get; set; }
    [NotMapped] public Uri? GameArtwork => BundledMapCatalog.Asset(Config?.GameImage);
    [NotMapped] public Uri? GameBanner => Path.GetDirectoryName(Config?.GameImage) is { Length: > 0 } directory
        ? BundledMapCatalog.Asset(Path.Combine(directory, "banner.jpg")) : null;
    [NotMapped] public Uri? MapArtwork => BundledMapCatalog.Asset(Config?.MapImage);
    public ICollection<Group> Groups { get; set; } = [];
    public ICollection<Timeline> Timelines { get; set; } = [];
    public ICollection<DownloadTask> DownloadTasks { get; set; } = [];
}
