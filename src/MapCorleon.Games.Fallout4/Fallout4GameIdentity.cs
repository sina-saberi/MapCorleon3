using MapCorleon.Contracts;

namespace MapCorleon.Games.Fallout4;

internal static class Fallout4GameIdentity
{
    public const string ProcessName = "Fallout4";

    public static bool IsCommonwealth(GameMapDescriptor map) =>
        Normalize(map.GameName) == "fallout4" && Normalize(map.MapName) == "commonwealth";

    private static string Normalize(string value) => new(value
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());
}
