using MapCorleon.Contracts;
using MapCorleon.Games.Fallout4;
using MapCorleon.Games.Kcd2;
using MapCorleon.Models;

namespace MapCorleon.Services;

internal sealed record DesktopOverlayProfile(
    string Key, string Name, string ProcessName, string? ExecutableVersion,
    int DefaultRadius, int MinRadius, int MaxRadius, string RadiusUnits)
{
    public static DesktopOverlayProfile Fallout4 { get; } = new(
        Fallout4MapProfile.OverlaySettingsKey, "Fallout 4", "Fallout4", null,
        GameOverlaySettings.DefaultRadius, GameOverlaySettings.MinRadius, GameOverlaySettings.MaxRadius, "Fallout 4 units");

    public static DesktopOverlayProfile Kcd2 { get; } = new(
        Kcd2MapProfile.OverlaySettingsKey, "KCD2", Kcd2MapProfile.ProcessName, Kcd2MapProfile.ExecutableVersion,
        75, 10, 500, "meters");

    public static (DesktopOverlayProfile Game, Func<double, double, double> DistanceSquared)? ForMap(GameMapDescriptor map)
    {
        if (Fallout4MapProfile.ForMap(map) is { } fallout) return (Fallout4, fallout.GameDistanceSquared);
        if (Kcd2MapProfile.ForMap(map) is { } kcd2) return (Kcd2, kcd2.GameDistanceSquared);
        return null;
    }

    public GameOverlaySettings GetSettings(ISettingsService settings) =>
        settings.Current.GameOverlays.GetValueOrDefault(Key) ?? GameOverlaySettings.Default with { NearbyRadius = DefaultRadius };
}
