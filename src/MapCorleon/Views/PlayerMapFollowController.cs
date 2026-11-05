namespace MapCorleon.Views;

public sealed class PlayerMapFollowController
{
    public bool CanFollow { get; private set; }
    public bool IsFollowing { get; private set; }
    public bool IsPaused { get; private set; }
    public bool ShouldCenter => IsFollowing && !IsPaused;

    public void SetPlayerAvailable(bool available)
    {
        CanFollow = available;
        if (!available) StopFollowing();
    }

    public bool Toggle()
    {
        if (!CanFollow) return false;
        IsFollowing = !IsFollowing;
        IsPaused = false;
        return IsFollowing;
    }

    public void Pause() => IsPaused = IsFollowing;

    public void Resume() => IsPaused = false;

    public void StopFollowing()
    {
        IsFollowing = false;
        IsPaused = false;
    }
}
