using MapCorleon.Contracts;
using MapCorleon.Models;

namespace MapCorleon.Services;

internal sealed class SkyrimNearbyLocations
{
    // About 2,500 Skyrim world units under the Tamriel world map calibration.
    private const double EnterRadius = 0.0034;
    private const double ExitRadius = 0.0040;
    private readonly HashSet<int> dismissed = [];

    public IReadOnlyList<Card> Find(PlayerLocation? player, IReadOnlyList<Location> locations, GameOverlaySettings? preferences = null)
    {
        if (player is null) return [];
        preferences ??= GameOverlaySettings.Default;
        var radiusScale = (double)Math.Clamp(preferences.NearbyRadius, GameOverlaySettings.MinRadius,
            GameOverlaySettings.MaxRadius) / GameOverlaySettings.DefaultRadius;
        var enterRadius = EnterRadius * radiusScale;
        var exitRadius = ExitRadius * radiusScale;
        var excludedCategories = preferences.ExcludedCategories ?? [];
        var candidates = new List<(Location Location, double DistanceSquared)>();
        var stillNearby = new HashSet<int>();
        foreach (var location in locations)
        {
            var x = location.Longitude - player.Longitude;
            var y = location.Latitude - player.Latitude;
            var distanceSquared = x * x + y * y;
            if (distanceSquared <= exitRadius * exitRadius && dismissed.Contains(location.Id))
                stillNearby.Add(location.Id);
            else if (distanceSquared <= enterRadius * enterRadius && location.Marks.Count == 0 &&
                     !excludedCategories.Contains(location.Category?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                candidates.Add((location, distanceSquared));
        }
        dismissed.IntersectWith(stillNearby);

        return candidates.OrderBy(candidate => candidate.DistanceSquared)
            .ThenBy(candidate => candidate.Location.Id)
            .Take(3)
            .Select(candidate => new Card(candidate.Location.Id, LimitText(candidate.Location.Title),
                LimitText(candidate.Location.Category?.Name ?? string.Empty)))
            .ToList();
    }

    public void Dismiss(int locationId) => dismissed.Add(locationId);

    public void Reset() => dismissed.Clear();

    private static string LimitText(string text)
    {
        const int limit = 180;
        if (text.Length <= limit) return text;
        var length = char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit;
        return text[..length] + "…";
    }

    internal sealed record Card(int Id, string Title, string Category);
}
