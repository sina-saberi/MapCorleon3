using MapCorleon.Contracts;
using MapCorleon.Games.Fallout4;
using MapCorleon.Models;
using MapCorleon.Services;

namespace MapCorleon.Tests;

public sealed class Fallout4OverlayTests
{
    [Fact]
    public void AlertExpiresAndReappearsOnlyAfterLeavingRange()
    {
        var alerts = new OverlayNearbyAlerts();
        OverlayNearbyLocations.Card[] nearby = [new(1, "Sanctuary", "Settlement")];
        var duration = TimeSpan.FromSeconds(4);

        Assert.Single(alerts.Update(nearby, TimeSpan.Zero, duration));
        Assert.Empty(alerts.Update(nearby, duration, duration));
        Assert.Empty(alerts.Update(nearby, duration * 2, duration));

        Assert.Empty(alerts.Update([], duration * 3, duration));
        Assert.Single(alerts.Update(nearby, duration * 4, duration));
    }

    [Fact]
    public void NewNearbyLocationCanAlertWhilePreviousLocationStaysInRange()
    {
        var alerts = new OverlayNearbyAlerts();
        var first = new OverlayNearbyLocations.Card(1, "First", "Settlement");
        var second = new OverlayNearbyLocations.Card(2, "Second", "Settlement");
        var duration = TimeSpan.FromSeconds(4);

        alerts.Update([first], TimeSpan.Zero, duration);

        Assert.Equal(second, Assert.Single(alerts.Update([first, second], duration, duration)));
    }

    [Theory]
    [InlineData("Commonwealth")]
    [InlineData("Far Harbor")]
    [InlineData("Nuka-World")]
    public void NearbyRadiusIsMeasuredInGameUnits(string map)
    {
        var profile = Fallout4MapProfile.ForMap(new GameMapDescriptor(1, "Fallout 4", map))!;
        var player = profile.Project(new GamePosition(10_000, 10_000, 200));
        var near = profile.Project(new GamePosition(11_400, 11_400, 200));
        var far = profile.Project(new GamePosition(12_000, 12_000, 200));
        var locations = new[]
        {
            new Location { Id = 1, Title = "Near", Latitude = near.Latitude, Longitude = near.Longitude },
            new Location { Id = 2, Title = "Far", Latitude = far.Latitude, Longitude = far.Longitude }
        };

        var cards = new OverlayNearbyLocations().Find(player, locations,
            GameOverlaySettings.Default, profile.GameDistanceSquared);

        Assert.Equal(1, Assert.Single(cards).Id);
    }
}
