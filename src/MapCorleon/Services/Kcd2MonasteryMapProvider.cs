using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using MapCorleon.Contracts;
using MapCorleon.Games.Kcd2;

namespace MapCorleon.Services;

internal sealed class Kcd2MonasteryMapProvider(IMapImportSink sink, ISettingsService settings) : IMapProvider
{
    private const string TileHost = "kcd2-local.invalid";
    private const string MapTexture = "Libs/UI/Textures/Maps/klaster_0.dds";
    private const string TileSet = "sedletz-v1";
    private static readonly IReadOnlyDictionary<int, TileRange> Bounds = new Dictionary<int, TileRange>
    {
        [10] = new(512, 512, 511, 511),
        [11] = new(1024, 1025, 1022, 1023)
    };

    public string Key => "kcd2-local";
    public string DisplayName => "KCD2 Sedletz Monastery (installed game)";
    public bool UsesLocalFile => true;

    public async Task<ImportResult> ImportAsync(MapImportRequest request, IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(request.PageUrl, UriKind.Absolute, out var archive) || !archive.IsFile ||
            !Path.GetFileName(archive.LocalPath).Equals("IPL_GameData.pak", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select Data\\IPL_GameData.pak from your Kingdom Come: Deliverance II installation.");

        var storage = TileDirectory;
        var map = await Task.Run(() => ReadInstalledMap(archive.LocalPath, storage, progress, ct), ct);
        return await sink.UpsertAsync(Kcd2MapProfile.GameName, map, progress, ct);
    }

    public Task<byte[]?> DownloadTileAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(TileHost, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<byte[]?>(null);

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not [TileSet, var zText, var xText, var yText] ||
            !int.TryParse(zText, out var z) || !int.TryParse(xText, out var x) || !int.TryParse(yText, out var y) ||
            !Bounds.TryGetValue(z, out var range) ||
            x < range.MinX || x > range.MaxX || y < range.MinY || y > range.MaxY)
            return Task.FromResult<byte[]?>(null);

        return ReadTileAsync(TilePath(TileDirectory, z, x, y), ct);
    }

    private string TileDirectory => Path.Combine(settings.Current.StorageDirectory, Key, TileSet);

    private static async Task<byte[]?> ReadTileAsync(string path, CancellationToken ct) =>
        File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;

    private ImportedMap ReadInstalledMap(string archivePath, string tileDirectory,
        IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var dataDirectory = Path.GetDirectoryName(archivePath)!;
        var levelPath = Path.Combine(dataDirectory, "Levels", "klaster", "level.pak");
        if (!File.Exists(levelPath))
            throw new FileNotFoundException("Sedletz Monastery level data is missing from this installation.", levelPath);

        progress?.Report(new("Reading Sedletz game map", 0, 2));
        using var archive = ZipFile.OpenRead(archivePath);
        var header = ReadEntry(archive, MapTexture, 4096);
        var mip = ReadEntry(archive, MapTexture + ".4", 512 * 512 / 2);
        var image = DecodeMapTexture(header, mip);
        var locations = ReadLocations(dataDirectory, levelPath, ct);
        if (locations.Count == 0)
            throw new InvalidDataException("The Sedletz level contains no recognizable map locations.");

        progress?.Report(new("Preparing local map tiles", 1, 2));
        WriteTile(new TransformedBitmap(image, new ScaleTransform(.5, .5)), TilePath(tileDirectory, 10, 512, 511));
        for (var x = 0; x < 2; x++)
        for (var y = 0; y < 2; y++)
        {
            ct.ThrowIfCancellationRequested();
            WriteTile(new CroppedBitmap(image, new Int32Rect(x * 256, y * 256, 256, 256)),
                TilePath(tileDirectory, 11, 1024 + x, 1022 + y));
        }

        var center = Kcd2MapProfile.Monastery.Project(new GamePosition(1280, 1088, 0));
        return new ImportedMap(Key, -177130004, Kcd2MapProfile.MonasteryName, center.Latitude, center.Longitude, 10,
            $"https://{TileHost}/{TileSet}/{{z}}/{{x}}/{{y}}", 10, 11, Bounds, null,
            [new ImportedGroup(1, "Sedletz Monastery", true, [new ImportedCategory(1, 1, "Locations", true, null, null)])],
            locations);
    }

    private static IReadOnlyList<ImportedLocation> ReadLocations(string dataDirectory, string levelPath, CancellationToken ct)
    {
        var labels = LoadEnglishLabels(dataDirectory);
        using var level = ZipFile.OpenRead(levelPath);
        var locations = new Dictionary<long, ImportedLocation>();
        foreach (var entry in level.Entries.Where(entry =>
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                     (entry.FullName.StartsWith("layers/", StringComparison.OrdinalIgnoreCase) ||
                      entry.FullName.StartsWith("objects_", StringComparison.OrdinalIgnoreCase))))
        {
            ct.ThrowIfCancellationRequested();
            using var xml = entry.Open();
            foreach (var poi in XDocument.Load(xml).Descendants("Object"))
            {
                if ((string?)poi.Attribute("Type") != "POI" || (string?)poi.Attribute("Name") is not { Length: > 0 } name ||
                    !TryReadPosition((string?)poi.Attribute("Pos"), out var position))
                    continue;

                var id = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(name))) & long.MaxValue;
                var labelKey = ((string?)poi.Attribute("CustomLabel"))?.TrimStart('@') ?? string.Empty;
                var label = labels.GetValueOrDefault(labelKey) ?? (name == "levelSwitch_POI"
                    ? "Road to Kuttenberg"
                    : Humanize(name));
                var point = Kcd2MapProfile.Monastery.Project(position);
                locations.TryAdd(id, new ImportedLocation(id, 1, label, string.Empty, point.Latitude, point.Longitude, []));
            }
        }
        return locations.Values.ToArray();
    }

    private static Dictionary<string, string> LoadEnglishLabels(string dataDirectory)
    {
        var file = Path.Combine(dataDirectory, "..", "Localization", "English_xml.pak");
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(file)) return labels;
        using var archive = ZipFile.OpenRead(file);
        var entry = archive.GetEntry("text_ui_menus.xml");
        if (entry is null) return labels;
        using var xml = entry.Open();
        foreach (var row in XDocument.Load(xml).Descendants("Row"))
        {
            var cells = row.Elements("Cell").Take(2).Select(cell => cell.Value).ToArray();
            if (cells is [var key, var value] && key.StartsWith("ui_maplegend_", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(value))
                labels.TryAdd(key, value);
        }
        return labels;
    }

    private static bool TryReadPosition(string? value, out GamePosition position)
    {
        if (value?.Split(',') is [var x, var y, var z] &&
            float.TryParse(x, CultureInfo.InvariantCulture, out var gameX) &&
            float.TryParse(y, CultureInfo.InvariantCulture, out var gameY) &&
            float.TryParse(z, CultureInfo.InvariantCulture, out var gameZ) &&
            float.IsFinite(gameX) && float.IsFinite(gameY) && float.IsFinite(gameZ))
        {
            position = new GamePosition(gameX, gameY, gameZ);
            return true;
        }
        position = new GamePosition(0, 0, 0);
        return false;
    }

    private static string Humanize(string name)
    {
        var text = name.Replace("_POI", "").Replace('_', ' ');
        var spaced = System.Text.RegularExpressions.Regex.Replace(text, "([a-z])([A-Z])", "$1 $2");
        return spaced.Length == 0 ? "Location" : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private static byte[] ReadEntry(ZipArchive archive, string name, int maximumSize)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing Sedletz map texture {name}.");
        if (entry.Length > maximumSize) throw new InvalidDataException("Unexpected Sedletz map texture size.");
        using var data = entry.Open();
        var bytes = new byte[(int)entry.Length];
        data.ReadExactly(bytes);
        return bytes;
    }

    private static BitmapSource DecodeMapTexture(byte[] header, byte[] blocks)
    {
        const int size = 512;
        if (header.Length < 128 || !header.AsSpan(0, 4).SequenceEqual("DDS "u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)) != size ||
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16)) != size ||
            !header.AsSpan(84, 4).SequenceEqual("DXT1"u8) || blocks.Length != size * size / 2)
            throw new InvalidDataException("The installed Sedletz texture does not match its expected format.");

        var pixels = new uint[size * size];
        Span<uint> palette = stackalloc uint[4];
        var offset = 0;
        for (var y = 0; y < size; y += 4)
        for (var x = 0; x < size; x += 4)
        {
            var first = BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(offset));
            var second = BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(offset + 2));
            palette[0] = Rgb565(first);
            palette[1] = Rgb565(second);
            palette[2] = Mix(palette[0], palette[1], first > second ? 2u : 1u, first > second ? 3u : 2u);
            palette[3] = first > second ? Mix(palette[0], palette[1], 1, 3) : 0;
            var indices = BinaryPrimitives.ReadUInt32LittleEndian(blocks.AsSpan(offset + 4));
            for (var row = 0; row < 4; row++)
            for (var col = 0; col < 4; col++)
            {
                pixels[(y + row) * size + x + col] = palette[(int)(indices & 3)];
                indices >>= 2;
            }
            offset += 8;
        }
        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * sizeof(uint));
    }

    private static uint Rgb565(uint color) => 0xff000000 |
        ((color >> 11) * 255 / 31 << 16) | (((color >> 5) & 63) * 255 / 63 << 8) | ((color & 31) * 255 / 31);

    private static uint Mix(uint a, uint b, uint weight, uint denominator)
    {
        uint Blend(int shift) => (((a >> shift & 255) * weight + (b >> shift & 255) * (denominator - weight)) / denominator) << shift;
        return 0xff000000 | Blend(16) | Blend(8) | Blend(0);
    }

    private static string TilePath(string directory, int z, int x, int y) => Path.Combine(directory, $"{z}-{x}-{y}.png");

    private static void WriteTile(BitmapSource source, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = File.Create(temporary)) encoder.Save(stream);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
