using System.ComponentModel;
using MapCorleon.Contracts;
using MapCorleon.Models;

namespace MapCorleon.Services;

public enum PlayerTrackingState
{
    Inactive,
    GameNotRunning,
    StartingGame,
    TrackingPlayer,
    PlayerIsInterior,
    UnableToReadLocation,
    UnsupportedGameVersion
}

public interface IPlayerLocationCoordinator : INotifyPropertyChanged
{
    PlayerLocation? CurrentLocation { get; }
    PlayerTrackingState State { get; }
    string StatusText { get; }
    bool Supports(Map map);
    PlayerLocationProviderMetadata? GetProviderMetadata(Map map);
    Task SetActiveMapAsync(Map? map, CancellationToken ct = default);
    void NotifyGameStarting();
}
