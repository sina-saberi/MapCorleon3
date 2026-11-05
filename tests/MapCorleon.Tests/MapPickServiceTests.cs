using MapCorleon.Services;

namespace MapCorleon.Tests;

public sealed class MapPickServiceTests
{
    [Fact]
    public async Task CompletesPendingPickAndPublishesStateChanges()
    {
        var service = new MapPickService();
        var stateChanges = 0;
        service.PickingChanged += (_, _) => stateChanges++;

        var pending = service.PickAsync();

        Assert.True(service.IsPicking);
        Assert.True(service.Complete(new MapPosition(10.5, 20.25)));
        Assert.Equal(new MapPosition(10.5, 20.25), await pending);
        Assert.False(service.IsPicking);
        Assert.Equal(2, stateChanges);
    }

    [Fact]
    public async Task CancelEndsPickWithoutReturningAStalePosition()
    {
        var service = new MapPickService();
        var pending = service.PickAsync();

        Assert.True(service.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(service.IsPicking);
        Assert.False(service.Complete(new MapPosition(1, 2)));
    }

    [Fact]
    public async Task RejectsConcurrentPickRequests()
    {
        var service = new MapPickService();
        _ = service.PickAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PickAsync());
    }
}
