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
    public void ToggleEnablesFollowAndUserNavigationStopsIt()
    {
        var controller = new PlayerMapFollowController();
        controller.SetPlayerAvailable(true);

        Assert.True(controller.Toggle());
        Assert.True(controller.IsFollowing);

        controller.StopFollowing();
        Assert.False(controller.IsFollowing);
        Assert.True(controller.CanFollow);
    }

    [Fact]
    public void LosingPlayerDisablesFollow()
    {
        var controller = new PlayerMapFollowController();
        controller.SetPlayerAvailable(true);
        controller.Toggle();

        controller.SetPlayerAvailable(false);

        Assert.False(controller.CanFollow);
        Assert.False(controller.IsFollowing);
    }
}
