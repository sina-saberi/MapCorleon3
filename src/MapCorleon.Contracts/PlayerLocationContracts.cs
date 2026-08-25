namespace MapCorleon.Contracts;

public sealed record GamePosition(float X, float Y, float Z);

public sealed record PlayerLocation(
    double Latitude,
    double Longitude,
    float GameX,
    float GameY,
    float GameZ);

public enum PlayerLocationReadStatus
{
    Success,
    GameNotRunning,
    UnsupportedGameVersion,
    UnableToRead
}

public sealed record PlayerLocationReadResult(
    PlayerLocationReadStatus Status,
    PlayerLocation? Location = null,
    string? Diagnostic = null)
{
    public static PlayerLocationReadResult Success(PlayerLocation location) =>
        new(PlayerLocationReadStatus.Success, location);
}

public sealed record PlayerLocationProviderMetadata(
    string SettingsTitle,
    string SettingsDescription);

public interface IPlayerLocationProvider
{
    string Key { get; }
    PlayerLocationProviderMetadata Metadata { get; }
    bool Supports(GameMapDescriptor map);
    Task<PlayerLocationReadResult> ReadAsync(GameMapDescriptor map, CancellationToken ct);
    void Detach();
}
