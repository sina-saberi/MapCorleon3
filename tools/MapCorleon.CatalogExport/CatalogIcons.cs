using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MapCorleon.Contracts;

namespace MapCorleon.MapGenie;

internal sealed class CatalogIcons(string fileName, Dictionary<string, List<ImportedIconLayer>> glyphs)
{
    public string FileName => fileName;

    public IReadOnlyList<ImportedIconLayer>? Find(string? key)
    {
        if (key is null) return null;
        foreach (var candidate in key.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (glyphs.TryGetValue(candidate, out var code) ||
                glyphs.TryGetValue("icon-" + candidate, out code) ||
                glyphs.TryGetValue("icon_" + candidate, out code))
                return code;
        return null;
    }

    public static async Task<CatalogIcons> SaveAsync(BrowserAssets assets, string directory, CancellationToken ct)
    {
        var glyphs = new Dictionary<string, List<ImportedIconLayer>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match rule in Regex.Matches(assets.Css!, """(?<selector>[^{}]+)\{(?<body>[^{}]*?content\s*:\s*['"]\\(?<code>[a-fA-F0-9]{1,6})['"][^{}]*)\}"""))
        {
            var code = int.Parse(rule.Groups["code"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var body = rule.Groups["body"].Value;
            var color = Regex.Match(body, @"\bcolor\s*:\s*(?<value>#[0-9a-fA-F]{6})").Groups["value"].Value;
            var rgb = Regex.Match(body, @"\bcolor\s*:\s*rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
            if (rgb.Success)
                color = $"#{byte.Parse(rgb.Groups[1].Value):X2}{byte.Parse(rgb.Groups[2].Value):X2}{byte.Parse(rgb.Groups[3].Value):X2}";
            foreach (var selector in rule.Groups["selector"].Value.Split(','))
            {
                var classes = Regex.Matches(selector, @"\.([\w-]+)");
                if (classes.Count == 0) continue;
                var key = classes[^1].Groups[1].Value;
                if (classes.Count > 1 && Regex.IsMatch(key, @"^path\d+$"))
                    key = classes[^2].Groups[1].Value;
                if (!glyphs.TryGetValue(key, out var layers)) glyphs.Add(key, layers = []);
                var layer = new ImportedIconLayer(code, color.Length > 0 ? color : null);
                if (!layers.Contains(layer)) layers.Add(layer);
            }
        }
        var font = Convert.FromBase64String(assets.Font!);
        if (font.AsSpan().StartsWith("wOFF"u8)) font = DecodeWoff(font);
        var extension = font.AsSpan().StartsWith("OTTO"u8) ? ".otf" : ".ttf";
        var filename = "icons-" + Convert.ToHexString(SHA256.HashData(font))[..12].ToLowerInvariant() + extension;
        await File.WriteAllBytesAsync(Path.Combine(directory, filename), font, ct);
        return new(filename, glyphs);
    }

    // WPF loads SFNT fonts. WOFF 1 stores the same tables with optional zlib compression.
    private static byte[] DecodeWoff(byte[] woff)
    {
        if (woff.Length < 44 || !woff.AsSpan().StartsWith("wOFF"u8))
            throw new InvalidDataException("The downloaded icon font is not WOFF 1.");
        int count = Read16(woff, 12), size = Read32(woff, 16);
        if (count < 1 || 44 + 20 * count > woff.Length || size < 12 + 16 * count || size > 20_000_000)
            throw new InvalidDataException("Invalid WOFF table directory.");
        var font = new byte[size];
        woff.AsSpan(4, 4).CopyTo(font);
        var power = 1;
        var selector = 0;
        while (power * 2 <= count) { power *= 2; selector++; }
        Write16(4, count); Write16(6, power * 16); Write16(8, selector); Write16(10, (count - power) * 16);
        var offset = 12 + 16 * count;
        for (var i = 0; i < count; i++)
        {
            int from = 44 + 20 * i, to = 12 + 16 * i;
            int input = Read32(woff, from + 4), compressed = Read32(woff, from + 8), length = Read32(woff, from + 12);
            if (input < 0 || compressed < 0 || length < compressed || compressed > woff.Length - input || length > font.Length - offset)
                throw new InvalidDataException("Invalid WOFF table range.");
            woff.AsSpan(from, 4).CopyTo(font.AsSpan(to));
            woff.AsSpan(from + 16, 4).CopyTo(font.AsSpan(to + 4));
            BinaryPrimitives.WriteInt32BigEndian(font.AsSpan(to + 8), offset);
            BinaryPrimitives.WriteInt32BigEndian(font.AsSpan(to + 12), length);
            if (compressed == length) woff.AsSpan(input, length).CopyTo(font.AsSpan(offset));
            else
            {
                using var data = new MemoryStream(woff, input, compressed);
                using var zlib = new ZLibStream(data, CompressionMode.Decompress);
                zlib.ReadExactly(font.AsSpan(offset, length));
            }
            offset += (length + 3) & ~3;
        }
        return font;

        void Write16(int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(offset), (ushort)value);
        static int Read16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        static int Read32(byte[] data, int offset) => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
    }
}
