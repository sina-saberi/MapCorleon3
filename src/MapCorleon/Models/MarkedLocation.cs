namespace MapCorleon.Models;

public sealed class MarkedLocation : IEntity
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public int TimelineId { get; set; }
    public DateTime MarkedAtUtc { get; set; } = DateTime.UtcNow;

    public Location? Location { get; set; }
    public Timeline? Timeline { get; set; }
}
