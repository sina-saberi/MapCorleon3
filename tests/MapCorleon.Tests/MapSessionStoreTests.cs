using System.IO;
using MapCorleon.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MapCorleon.Tests;

public sealed class MapSessionStoreTests
{
    [Fact]
    public void ReloadsIndependentMapSessionsAndRemembersClosingDetails()
    {
        var directory = Directory.CreateTempSubdirectory("MapCorleon-session-");
        try
        {
            var path = Path.Combine(directory.FullName, "map-sessions.json");
            var viewport = new MapViewportState(123, 456, .25);
            var window = new WindowPlacementState(-1200, 100, -100, 850, true);
            var initial = Open(path);
            initial.Update("map-a", state => state with
            {
                Viewport = viewport,
                Window = window,
                LocationId = 12,
                TimelineId = 5,
                CollapsedGroups = [8]
            });
            initial.Update("map-b", state => state with { LocationId = 34, CollapsedGroups = [9] });

            var reopened = Open(path);
            Assert.Equal(viewport, reopened.Read("map-a").Viewport);
            Assert.Equal(window, reopened.Read("map-a").Window);
            Assert.Equal(12, reopened.Read("map-a").LocationId);
            Assert.Equal(5, reopened.Read("map-a").TimelineId);
            Assert.Equal(new[] { 8 }, reopened.Read("map-a").CollapsedGroups);
            Assert.Null(reopened.Read("map-b").Viewport);
            Assert.Null(reopened.Read("map-b").Window);

            reopened.Update("map-a", state => state with { LocationId = null });
            var afterClose = Open(path);
            Assert.Null(afterClose.Read("map-a").LocationId);
            Assert.Equal(34, afterClose.Read("map-b").LocationId);
            Assert.Equal(new[] { 9 }, afterClose.Read("map-b").CollapsedGroups);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static MapSessionStore Open(string path) => new(path, NullLogger<MapSessionStore>.Instance);
}
