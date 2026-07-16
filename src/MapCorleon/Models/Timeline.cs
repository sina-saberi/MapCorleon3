namespace MapCorleon.Models;

public sealed class Timeline : IEntity
{
    public int Id { get; set; }
    public int MapId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }

    public Map? Map { get; set; }
    public ICollection<Pin> Pins { get; set; } = [];
    public ICollection<MarkedLocation> Marks { get; set; } = [];
    public ICollection<Location> CustomLocations { get; set; } = [];
}
