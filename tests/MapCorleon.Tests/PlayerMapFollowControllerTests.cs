using MapCorleon.Views;

namespace MapCorleon.Tests;

public sealed class PlayerMapFollowControllerTests
{
    [Fact]
    public void CannotFollowUntilPlayerIsAvailable()
    {
        var controller = new PlayerMapFollowController();

        Assert.False(controller.Toggle());
        Assert.False(controller.IsFollowing);
    }

    [Fact]
    public void UserNavigationPausesCenteringUntilFollowResumes()
    {
        var controller = new PlayerMapFollowController();
        controller.SetPlayerAvailable(true);

        Assert.True(controller.Toggle());
        Assert.True(controller.ShouldCenter);

        controller.Pause();
        Assert.True(controller.IsFollowing);
        Assert.False(controller.ShouldCenter);

        controller.Resume();
        Assert.True(controller.ShouldCenter);
    }

    [Fact]
    public void TurningFollowOffWhilePausedPreventsResume()
    {
        var controller = new PlayerMapFollowController();
        controller.SetPlayerAvailable(true);
        controller.Toggle();
        controller.Pause();

        Assert.False(controller.Toggle());
        controller.Resume();
        Assert.False(controller.ShouldCenter);
    }

    [Fact]
    public void LosingPlayerDisablesFollow()
    {
        var controller = new PlayerMapFollowController();
        controller.SetPlayerAvailable(true);
        controller.Toggle();
        controller.Pause();

        controller.SetPlayerAvailable(false);

        Assert.False(controller.CanFollow);
        Assert.False(controller.IsFollowing);
        Assert.False(controller.IsPaused);
    }
}
