using MapCorleon.Contracts;

namespace MapCorleon.Games.Skyrim;

public static class SkyrimGameIdentity
{
    public static bool IsSkyrim(GameMapDescriptor map)
    {
        var game = Normalize(map.GameName);
        return game is "skyrim" or "skyrimspecialedition" or "theelderscrollsvskyrimspecialedition";
    }

    public static bool IsWorldMap(GameMapDescriptor map) =>
        IsSkyrim(map) && Normalize(map.MapName) == "skyrim";

    private static string Normalize(string? value) => new((value ?? string.Empty)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());
}
