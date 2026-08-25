using MapCorleon.Contracts;
using MapCorleon.Models;

namespace MapCorleon.Services;

internal static class GameMapDescriptorFactory
{
    public static GameMapDescriptor Create(Map map) => new(
        map.Id,
        map.Game?.Name ?? string.Empty,
        map.Name,
        map.Config?.MinLatitude,
        map.Config?.MaxLatitude,
        map.Config?.MinLongitude,
        map.Config?.MaxLongitude);
}
