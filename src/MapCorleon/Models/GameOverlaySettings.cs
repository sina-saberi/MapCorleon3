namespace MapCorleon.Models;

public sealed record GameOverlaySettings
{
    public const int DefaultRadius = 2500;
    public const int MinRadius = 250;
    public const int MaxRadius = 10000;
    public const int DefaultNotificationSeconds = 4;
    public const int MinNotificationSeconds = 1;
    public const int MaxNotificationSeconds = 15;

    public static GameOverlaySettings Default { get; } = new();

    public int NearbyRadius { get; init; } = DefaultRadius;
    public int NearbyNotificationSeconds { get; init; } = DefaultNotificationSeconds;
    public int? PanelScanCode { get; init; }
    public int? FollowScanCode { get; init; }
    public string[] ExcludedCategories { get; init; } = [];
}
