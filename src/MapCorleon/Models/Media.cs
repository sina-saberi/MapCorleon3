namespace MapCorleon.Models;

public sealed class Media : IEntity
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public long? ExternalId { get; set; }
    public required string ContentHash { get; set; }
    public required string RelativePath { get; set; }
    public string? SourceUrl { get; set; }
    public string? Caption { get; set; }

    public Location? Location { get; set; }
}
