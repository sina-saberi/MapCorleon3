namespace MapCorleon.Models;

public sealed class Location : IEntity
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public int? TimelineId { get; set; }
    public long? ExternalId { get; set; }
    public required string Title { get; set; }
    public string DescriptionMarkdown { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsCustom { get; set; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(DescriptionMarkdown);

    public Category? Category { get; set; }
    public Timeline? Timeline { get; set; }
    public ICollection<Media> Media { get; set; } = [];
    public ICollection<MarkedLocation> Marks { get; set; } = [];
}
