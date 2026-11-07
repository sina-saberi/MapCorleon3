using System.Text.Json;
using MapCorleon.Contracts;
using MapCorleon.MapGenie;

var listOnly = args.Length == 2 && args[0] == "--list";
if (listOnly) args = [args[1], ".", "1"];
if (args.Length is < 3 or > 6 || !Uri.TryCreate(args[0], UriKind.Absolute, out var url) ||
    url.Scheme is not ("http" or "https") || !int.TryParse(args[2], out var revision) || revision < 1)
{
    Console.Error.WriteLine("Usage: MapCorleon.CatalogExport <map URL> <catalog directory> <revision> [game image or -] [map image or -] [icon CSS URL]");
    Console.Error.WriteLine("       MapCorleon.CatalogExport --list <map URL>");
    return 2;
}

using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(3));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
var ct = lifetime.Token;
try
{
    var (game, map, assets) = await new MapGenieExporter().ReadAsync(url, args.ElementAtOrDefault(5), listOnly, ct);
    if (listOnly)
    {
        foreach (var link in assets.Links ?? []) Console.WriteLine(link);
        return 0;
    }
    var root = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(root);
    var fileName = $"{map.ExternalId}.map.json";
    var previous = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).SingleOrDefault();
    var key = string.Concat(game.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')).Trim('-');
    var directory = previous is null ? Directory.CreateDirectory(Path.Combine(root, key)).FullName : Path.GetDirectoryName(previous)!;
    key = Path.GetRelativePath(root, directory).Replace('\\', '/');
    var gameImage = CopyArt(args.ElementAtOrDefault(3), "cover");
    var mapImage = CopyArt(args.ElementAtOrDefault(4), $"map-{map.ExternalId}");
    var iconFont = default(string);
    if (assets is { Css: not null, Font: not null })
    {
        var icons = await CatalogIcons.SaveAsync(assets, directory, ct);
        iconFont = $"{key}/{icons.FileName}";
        map = map with
        {
            Groups = map.Groups.Select(group => group with
            {
                Categories = group.Categories.Select(category => category with
                {
                    IconLayers = icons.Find(category.IconKey)
                }).ToArray()
            }).ToArray()
        };
        Console.WriteLine($"Mapped {map.Groups.SelectMany(group => group.Categories).Count(category => category.IconLayers is { Count: > 0 })} category icons.");
    }
    else if (assets is { Sprite: not null, SpriteMetadata: not null })
    {
        var metadata = JsonSerializer.Deserialize<Dictionary<string, SpriteRect>>(assets.SpriteMetadata,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var png = Convert.FromBase64String(assets.Sprite);
        var atlasName = "markers-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png))[..12].ToLowerInvariant() + ".png";
        await File.WriteAllBytesAsync(Path.Combine(directory, atlasName), png, ct);
        map = map with
        {
            SpriteImageLocation = $"{key}/{atlasName}",
            Groups = map.Groups.Select(group => group with
            {
                Categories = group.Categories.Select(category => category with
                {
                    Sprite = category.IconKey is { } iconKey ? metadata.GetValueOrDefault(iconKey) : null
                }).ToArray()
            }).ToArray()
        };
        Console.WriteLine($"Mapped {map.Groups.SelectMany(group => group.Categories).Count(category => category.Sprite is not null)} category sprites.");
    }
    else
    {
        if (previous is not null)
            throw new InvalidDataException("No usable icon assets were retrieved. The existing catalog file was kept.");
        Console.Error.WriteLine("No usable icon font or sprite atlas found. Pass the game’s icon CSS URL if it has one.");
    }

    var output = Path.Combine(directory, fileName);
    var entry = new CatalogMap(game, revision, map, gameImage, mapImage, iconFont, url.AbsoluteUri);
    await File.WriteAllTextAsync(output + ".tmp", JsonSerializer.Serialize(entry), ct);
    File.Move(output + ".tmp", output, overwrite: true);
    Console.WriteLine($"{game} / {map.Name}: {map.Locations.Count} locations exported to {output}.");
    return 0;

    string? CopyArt(string? source, string name)
    {
        if (source is null or "-")
        {
            foreach (var extension in new[] { ".jpg", ".jpeg", ".png" })
                if (File.Exists(Path.Combine(directory, name + extension))) return $"{key}/{name}{extension}";
            return null;
        }
        var filename = name + Path.GetExtension(source).ToLowerInvariant();
        var destination = Path.Combine(directory, filename);
        if (!Path.GetFullPath(source).Equals(destination, StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, overwrite: true);
        return $"{key}/{filename}";
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
