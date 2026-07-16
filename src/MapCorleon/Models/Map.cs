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
    public ICollection<Group> Groups { get; set; } = [];
    public ICollection<Timeline> Timelines { get; set; } = [];
}
