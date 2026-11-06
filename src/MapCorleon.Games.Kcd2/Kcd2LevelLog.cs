using System.Text;
using System.Text.RegularExpressions;

namespace MapCorleon.Games.Kcd2;

internal sealed partial class Kcd2LevelLog(string path)
{
    private const int ChunkSize = 16 * 1024;
    private long lastLength = -1;
    private DateTime lastWrite;
    private string? currentLevel;

    public string? ReadLevel()
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var write = File.GetLastWriteTimeUtc(path);
        if (length == lastLength && write == lastWrite) return currentLevel;

        // Search from the end because the initial level may be far back in a long session.
        var buffer = new byte[ChunkSize];
        string? level = null;
        for (var end = length; end > 0;)
        {
            var start = Math.Max(0, end - ChunkSize);
            stream.Position = start;
            var count = (int)(end - start);
            stream.ReadExactly(buffer.AsSpan(0, count));
            var matches = LevelLine().Matches(Encoding.UTF8.GetString(buffer, 0, count));
            if (matches.Count > 0)
            {
                level = matches[^1].Groups[1].Value;
                break;
            }
            if (start == 0 || (length > lastLength && lastLength >= 0 && start < lastLength))
            {
                level = length > lastLength ? currentLevel : null;
                break;
            }
            end = start + 128; // Keep a complete level banner across chunk boundaries.
        }

        currentLevel = level;
        lastLength = length;
        lastWrite = write;
        return currentLevel;
    }

    [GeneratedRegex(@"={4,} Loading level ([a-z0-9_]+) ={4,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelLine();
}
