namespace MapCorleon.Models;

public sealed class Pin : IEntity
{
    public int Id { get; set; }
    public int TimelineId { get; set; }
    public int? LocationId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Title { get; set; }

    public Timeline? Timeline { get; set; }
    public Location? Location { get; set; }
}
