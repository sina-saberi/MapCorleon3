namespace MapCorleon.Contracts;

public sealed record GameMapDescriptor(
    int MapId,
    string GameName,
    string MapName,
    double? MinLatitude = null,
    double? MaxLatitude = null,
    double? MinLongitude = null,
    double? MaxLongitude = null);
