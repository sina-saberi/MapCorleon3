namespace MapCorleon.Views;

public sealed class PlayerMapFollowController
{
    public bool CanFollow { get; private set; }
    public bool IsFollowing { get; private set; }

    public void SetPlayerAvailable(bool available)
    {
        CanFollow = available;
        if (!available) IsFollowing = false;
    }

    public bool Toggle()
    {
        if (!CanFollow) return false;
        IsFollowing = !IsFollowing;
        return IsFollowing;
    }

    public void StopFollowing() => IsFollowing = false;
}
