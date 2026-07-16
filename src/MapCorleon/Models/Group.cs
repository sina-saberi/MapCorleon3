namespace MapCorleon.Models;

public sealed class Group : IEntity
{
    public int Id { get; set; }
    public int MapId { get; set; }
    public long? ExternalId { get; set; }
    public required string Name { get; set; }
    public bool IsVisible { get; set; } = true;

    public Map? Map { get; set; }
    public ICollection<Category> Categories { get; set; } = [];
}
