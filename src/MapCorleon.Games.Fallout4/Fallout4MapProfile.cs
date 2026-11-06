using MapCorleon.Contracts;

namespace MapCorleon.Games.Fallout4;

public sealed record Fallout4MapProfile(
    uint WorldspaceId,
    double LongitudeFromX, double LongitudeFromY, double LongitudeOffset,
    double LatitudeFromX, double LatitudeFromY, double LatitudeOffset)
{
    public const string OverlaySettingsKey = "fallout4";

    // MapGenie maps 200, 201, and 202. DLC form IDs include the official master slots.
    public static Fallout4MapProfile? ForMap(GameMapDescriptor map) => Fallout4GameIdentity.IsFallout4(map)
        ? Fallout4GameIdentity.Normalize(map.MapName) switch
        {
            "commonwealth" => Commonwealth,
            "farharbor" => FarHarbor,
            "nukaworld" => NukaWorld,
            _ => null
        }
        : null;

    private static readonly Fallout4MapProfile Commonwealth = new(
        0x0000003C, 2.6522760470296115e-06, 2.3012160257107616e-09, -0.676360717502881,
        1.7485144174614332e-09, 2.6537831014047842e-06, 0.7621077104887968);

    private static readonly Fallout4MapProfile FarHarbor = new(
        0x03000B0F, 4.661345984452397e-06, 4.035904165451409e-09, -0.740905546666355,
        -6.687835708393264e-09, 4.724388937894753e-06, 0.7041434149211708);

    private static readonly Fallout4MapProfile NukaWorld = new(
        0x0600290F, 5.719391427702448e-06, 8.803035675482019e-09, -0.6623902199160263,
        -3.158899713179619e-08, 5.656844055118405e-06, 0.6576125436704727);

    // Invert the affine transform so the configured radius is in Fallout 4 world units.
    public double GameDistanceSquared(double longitudeDelta, double latitudeDelta)
    {
        var determinant = LongitudeFromX * LatitudeFromY - LongitudeFromY * LatitudeFromX;
        var x = (LatitudeFromY * longitudeDelta - LongitudeFromY * latitudeDelta) / determinant;
        var y = (LongitudeFromX * latitudeDelta - LatitudeFromX * longitudeDelta) / determinant;
        return x * x + y * y;
    }

    public PlayerLocation Project(GamePosition position) => new(
        LatitudeFromX * position.X + LatitudeFromY * position.Y + LatitudeOffset,
        LongitudeFromX * position.X + LongitudeFromY * position.Y + LongitudeOffset,
        position.X, position.Y, position.Z);
}
