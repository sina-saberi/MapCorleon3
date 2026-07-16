namespace MapCorleon.Models;

public sealed class Category : IEntity
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public long? ExternalId { get; set; }
    public required string Name { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? IconKey { get; set; }
    public int? IconX { get; set; }
    public int? IconY { get; set; }
    public int? IconWidth { get; set; }
    public int? IconHeight { get; set; }
    public double? IconPixelRatio { get; set; }

    public Group? Group { get; set; }
    public ICollection<Location> Locations { get; set; } = [];
}
