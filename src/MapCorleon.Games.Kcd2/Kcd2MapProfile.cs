using MapCorleon.Contracts;

namespace MapCorleon.Games.Kcd2;

public sealed record Kcd2MapProfile(
    string Level, double LongitudeFromX, double LongitudeFromY, double LongitudeOffset,
    double LatitudeFromX, double LatitudeFromY, double LatitudeOffset)
{
    public const string GameName = "Kingdom Come: Deliverance II";
    public const string ProcessName = "KingdomCome";
    public const string ExecutableVersion = "1.5.6.0";
    public const string OverlaySettingsKey = "kcd2";
    public const string MonasteryName = "Sedletz Monastery";

    public static Kcd2MapProfile? ForMap(GameMapDescriptor map) =>
        IsKcd2(map) ? Normalize(map.MapName) switch
        {
            "trosky" or "troskyregion" => Trosky,
            "kuttenberg" or "kuttenbergregion" => Kuttenberg,
            "sedletzmonastery" => Monastery,
            _ => null
        } : null;

    public static bool IsKcd2(GameMapDescriptor map) =>
        Normalize(map.GameName) is "kingdomcomedeliveranceii" or "kingdomcomedeliverance2";

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static readonly Kcd2MapProfile Trosky = new(
        "trosecko", .0002662490595464856, -.00000033746533450070614, -1.1874985458296379,
        .00000022703764756946466, .0002650460739654814, .15417893631552337);

    private static readonly Kcd2MapProfile Kuttenberg = new(
        "kutnohorsko", .00008589201395827487, -.00008596633406370767, -.7030443777340646,
        .00008589128333740049, .00008592295629514237, .37994335230006954);

    // Local-map bounds and rotation in Tables.pak. The image occupies one z10 Web Mercator tile.
    private const double TileDegrees = 360.0 / 1024;
    public static Kcd2MapProfile Monastery { get; } = new(
        "klaster", 1.5 * TileDegrees / 1062, -1.5 * TileDegrees / 1062, (3072 - 2913) * TileDegrees / 1062,
        1.5 * TileDegrees / 935, 1.5 * TileDegrees / 935, (2148 + 935 - 6144) * TileDegrees / 935);

    public PlayerLocation Project(GamePosition position)
    {
        var latitude = LatitudeFromX * position.X + LatitudeFromY * position.Y + LatitudeOffset;
        if (Level == Monastery.Level)
            latitude = Math.Atan(Math.Sinh(latitude * Math.PI / 180)) * 180 / Math.PI;
        return new(latitude, LongitudeFromX * position.X + LongitudeFromY * position.Y + LongitudeOffset,
            position.X, position.Y, position.Z);
    }

    public double GameDistanceSquared(double longitudeDelta, double latitudeDelta)
    {
        var determinant = LongitudeFromX * LatitudeFromY - LongitudeFromY * LatitudeFromX;
        var x = (LatitudeFromY * longitudeDelta - LongitudeFromY * latitudeDelta) / determinant;
        var y = (LongitudeFromX * latitudeDelta - LatitudeFromX * longitudeDelta) / determinant;
        return x * x + y * y;
    }
}
