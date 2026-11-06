using MapCorleon.Contracts;

namespace MapCorleon.Games.Skyrim;

public readonly record struct ProcessMemoryAddresses(nint X, nint Y, nint Z);

public sealed record SkyrimWorldMapMemoryLayout(
    nint PlayerParentCellOffset,
    nint CellFormIdOffset,
    nint CellFlagsOffset,
    nint CellWorldspaceOffset);

public sealed record SkyrimWorldMapCalibration(
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
    public bool TryConvert(GamePosition position, out PlayerLocation? location)
    {
        location = null;
        if (MaximumGameX <= MinimumGameX || MaximumGameY <= MinimumGameY) return false;
        if (position.X < MinimumGameX || position.X > MaximumGameX ||
            position.Y < MinimumGameY || position.Y > MaximumGameY) return false;

        var longitude = LongitudeFromX * position.X + LongitudeFromY * position.Y + LongitudeOffset;
        var latitude = LatitudeFromX * position.X + LatitudeFromY * position.Y + LatitudeOffset;
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude)) return false;
        location = new PlayerLocation(latitude, longitude, position.X, position.Y, position.Z);
        return true;
    }
}

public sealed record SkyrimWorldMapMemoryProfile(
    string Version,
    nint PositionOffset,
    nint PlayerCharacterSingletonOffset,
    SkyrimWorldMapMemoryLayout Layout,
    SkyrimWorldMapCalibration Calibration)
{
    // Tamriel's exterior grid spans cells -57..62 east/west and -43..46 south/north.
    // The affine coefficients are a least-squares fit against 27 exterior
    // Tamriel samples distributed across the imported Skyrim world map.
    public static SkyrimWorldMapMemoryProfile AnniversaryEdition1170 { get; } = new(
        "1.6.1170",
        0x338C8C0,
        // Skyrim AE 1.6.1170. Address Library ID 403521 resolves to RVA 0x31874F8
        // in versionlib-1-6-1170-0.bin. CommonLibSSE-NG PlayerCharacter::GetSingleton
        // dereferences the NiPointer<PlayerCharacter> stored at this relocation.
        // https://github.com/CharmedBaryon/CommonLibSSE-NG/blob/main/include/RE/Offsets.h
        0x31874F8,
        // TESObjectREFR::parentCell and TESObjectCELL fields for AE 1.6.1170.
        // https://github.com/CharmedBaryon/CommonLibSSE-NG/blob/main/include/RE/T/TESObjectREFR.h
        // https://github.com/CharmedBaryon/CommonLibSSE-NG/blob/main/include/RE/T/TESObjectCELL.h
        new SkyrimWorldMapMemoryLayout(
            PlayerParentCellOffset: 0x60,
            CellFormIdOffset: 0x14,
            CellFlagsOffset: 0x40,
            CellWorldspaceOffset: 0x128),
        new SkyrimWorldMapCalibration(
            -57 * 4096,
            63 * 4096,
            -43 * 4096,
            47 * 4096,
            LongitudeFromX: 1.3457349544834341e-06,
            LongitudeFromY: -1.1489646669630941e-08,
            LongitudeOffset: -1.0928894333008343,
            LatitudeFromX: 3.5170256592816117e-09,
            LatitudeFromY: 1.3449893590886038e-06,
            LatitudeOffset: 1.1259911832689498));

    // MapGenie Solstheim (map 18), fitted from Dragonborn.esm exterior markers.
    public static SkyrimWorldMapCalibration SolstheimCalibration { get; } = new(
        -1_000_000, 1_000_000, -1_000_000, 1_000_000,
        LongitudeFromX: 1.3398508635383147e-06,
        LongitudeFromY: 1.5984830683546392e-09,
        LongitudeOffset: -1.3402011667439906,
        LatitudeFromX: 4.1088921697685075e-10,
        LatitudeFromY: 1.3414745142952379e-06,
        LatitudeOffset: 1.2357093726382187);

    public bool SupportsVersion(string value) =>
        value.Equals(Version, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(Version + ".", StringComparison.OrdinalIgnoreCase);

    public ProcessMemoryAddresses GetPositionAddresses(nint moduleBase)
    {
        var x = moduleBase + PositionOffset;
        return new ProcessMemoryAddresses(x, x + sizeof(float), x + 2 * sizeof(float));
    }

    public nint GetPlayerCharacterSingletonAddress(nint moduleBase) =>
        moduleBase + PlayerCharacterSingletonOffset;
}
