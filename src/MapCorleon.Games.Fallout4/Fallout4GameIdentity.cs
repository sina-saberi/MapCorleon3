using MapCorleon.Contracts;

namespace MapCorleon.Games.Fallout4;

internal static class Fallout4GameIdentity
{
    public const string ProcessName = "Fallout4";

    public static bool IsFallout4(GameMapDescriptor map) => Normalize(map.GameName) == "fallout4";

    public static string Normalize(string value) => new(value
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());
}
