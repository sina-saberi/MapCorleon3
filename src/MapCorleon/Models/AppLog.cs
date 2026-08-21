namespace MapCorleon.Models;

public sealed class AppLog : IEntity
{
    public int Id { get; set; }
    public required string Type { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
