namespace MapCorleon.Services;

internal sealed class OverlayNearbyAlerts
{
    private readonly Dictionary<int, TimeSpan> expires = [];

    public IReadOnlyList<OverlayNearbyLocations.Card> Update(
        IReadOnlyList<OverlayNearbyLocations.Card> nearby, TimeSpan now, TimeSpan duration)
    {
        var inRange = nearby.Select(card => card.Id).ToHashSet();
        foreach (var id in expires.Keys.Where(id => !inRange.Contains(id)).ToArray())
            expires.Remove(id);

        foreach (var card in nearby)
            expires.TryAdd(card.Id, now + duration);

        return nearby.Where(card => expires[card.Id] > now).Take(3).ToArray();
    }

    public void Reset() => expires.Clear();
}
