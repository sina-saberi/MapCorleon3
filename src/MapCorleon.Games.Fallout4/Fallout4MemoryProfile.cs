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
}
