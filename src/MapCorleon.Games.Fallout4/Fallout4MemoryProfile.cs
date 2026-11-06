using MapCorleon.Contracts;

namespace MapCorleon.Games.Fallout4;

internal sealed record Fallout4MemoryProfile(
    Version RuntimeVersion,
    nint PlayerSingletonOffset,
    nint ParentCellOffset,
    nint PositionOffset,
    nint CellFlagsOffset,
    nint CellWorldspaceOffset)
{
    public const nint FormIdOffset = 0x14;
    public const uint PlayerFormId = 0x14;
    public const uint CommonwealthWorldspaceFormId = 0x3C;
    public const ushort InteriorFlag = 0x1;

    // F4SE v0.7.8 targets 1.11.221. See docs/fallout4-tracking.md for the layout sources.
    private static readonly Fallout4MemoryProfile Runtime111221 = new(
        new Version(1, 11, 221, 0),
        PlayerSingletonOffset: 0x032D22E0,
        ParentCellOffset: 0xB8,
        PositionOffset: 0xD0,
        CellFlagsOffset: 0x40,
        CellWorldspaceOffset: 0xC8);

    public static Fallout4MemoryProfile? ForVersion(string value) =>
        Version.TryParse(value, out var version) &&
        (version == Runtime111221.RuntimeVersion || version == new Version(1, 11, 221))
            ? Runtime111221
            : null;

    // Affine fit of Fallout4.esm Commonwealth map markers to MapGenie Commonwealth (map 200).
    public static PlayerLocation Project(GamePosition position) => new(
        1.7485144174614332e-09 * position.X + 2.6537831014047842e-06 * position.Y + 0.7621077104887968,
        2.6522760470296115e-06 * position.X + 2.3012160257107616e-09 * position.Y - 0.676360717502881,
        position.X,
        position.Y,
        position.Z);
}
