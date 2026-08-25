using System.ComponentModel;
using MapCorleon.Models;

namespace MapCorleon.Services;

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
    public static PlayerLocationReadResult Success(PlayerLocation location) => new(PlayerLocationReadStatus.Success, location);
}

public interface IPlayerLocationProvider
{
    string Key { get; }
    bool Supports(Map map);
    Task<PlayerLocationReadResult> ReadAsync(Map map, CancellationToken ct);
    void Detach();
}

public enum PlayerTrackingState
{
    Inactive,
    GameNotRunning,
    StartingGame,
    TrackingPlayer,
    UnableToReadLocation,
    UnsupportedGameVersion
}

public interface IPlayerLocationCoordinator : INotifyPropertyChanged
{
    PlayerLocation? CurrentLocation { get; }
    PlayerTrackingState State { get; }
    string StatusText { get; }
    bool Supports(Map map);
    Task SetActiveMapAsync(Map? map, CancellationToken ct = default);
    void NotifyGameStarting();
}
