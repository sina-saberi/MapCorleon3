using MapCorleon.Models;

namespace MapCorleon.Services;

public readonly record struct ProcessMemoryAddresses(nint X, nint Y, nint Z);

public sealed record SkyrimMapCalibration(
    string MapName,
    float MinimumGameX,
    float MaximumGameX,
    float MinimumGameY,
    float MaximumGameY,
    double LongitudeFromX,
    double LongitudeFromY,
    double LongitudeOffset,
    double LatitudeFromX,
    double LatitudeFromY,
    double LatitudeOffset)
{
    public bool TryConvert(GamePosition position, MapConfig? config, out PlayerLocation? location)
    {
        location = null;
        if (config is null || MaximumGameX <= MinimumGameX || MaximumGameY <= MinimumGameY) return false;
        if (position.X < MinimumGameX || position.X > MaximumGameX ||
            position.Y < MinimumGameY || position.Y > MaximumGameY) return false;

        var longitude = LongitudeFromX * position.X + LongitudeFromY * position.Y + LongitudeOffset;
        var latitude = LatitudeFromX * position.X + LatitudeFromY * position.Y + LatitudeOffset;
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude)) return false;
        location = new PlayerLocation(latitude, longitude, position.X, position.Y, position.Z);
        return true;
    }
}

public sealed record SkyrimMemoryProfile(
    string Version,
    nint PositionOffset,
    SkyrimMapCalibration Calibration)
{
    // Tamriel's exterior grid spans cells -57..62 east/west and -43..46 south/north;
    // each cell is 4096 game units. The affine coefficients were calibrated against
    // MapGenie markers at Sleeping Giant Inn, Treasure Chest I, and College of Winterhold.
    public static SkyrimMemoryProfile AnniversaryEdition1170 { get; } = new(
        "1.6.1170",
        0x338C8C0,
        new SkyrimMapCalibration(
            "Skyrim",
            -57 * 4096,
            63 * 4096,
            -43 * 4096,
            47 * 4096,
            LongitudeFromX: 1.3776345695272929e-06,
            LongitudeFromY: 2.5482703794042194e-08,
            LongitudeOffset: -1.0927358795483875,
            LatitudeFromX: 1.3455842776243513e-08,
            LatitudeFromY: 1.4272830273578014e-06,
            LatitudeOffset: 1.1294162431168082));

    public bool SupportsVersion(string value) =>
        value.Equals(Version, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(Version + ".", StringComparison.OrdinalIgnoreCase);

    public ProcessMemoryAddresses GetPositionAddresses(nint moduleBase)
    {
        var x = moduleBase + PositionOffset;
        return new ProcessMemoryAddresses(x, x + sizeof(float), x + 2 * sizeof(float));
    }
}
