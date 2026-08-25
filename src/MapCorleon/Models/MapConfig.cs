namespace MapCorleon.Models;

public sealed class MapConfig : IEntity
{
    public int Id { get; set; }
    public int MapId { get; set; }
    public double MinLatitude { get; set; }
    public double MaxLatitude { get; set; }
    public double MinLongitude { get; set; }
    public double MaxLongitude { get; set; }
    public double DefaultZoom { get; set; } = 2;
    public string Provider { get; set; } = "mapgenie";
    public string? TilePattern { get; set; }
    public int? MinZoom { get; set; }
    public int? MaxZoom { get; set; }
    public string? TileBoundsJson { get; set; }
    public string? SpriteImagePath { get; set; }
    public string? LaunchExecutablePath { get; set; }
    public string? LaunchArguments { get; set; }
    public string? LaunchWorkingDirectory { get; set; }

    public Map? Map { get; set; }
}
