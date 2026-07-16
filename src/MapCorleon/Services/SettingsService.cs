using System.IO;
using System.Text.Json;
using MapCorleon.Models;

namespace MapCorleon.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task SaveAsync(AppSettings settings);
}

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MapCorleon",
        "settings.json");

    public JsonSettingsService()
    {
        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
                ?? AppSettings.Default;
        }
        catch
        {
            Current = AppSettings.Default;
        }
    }

    public AppSettings Current { get; private set; }

    public async Task SaveAsync(AppSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(settings.StorageDirectory);
        Directory.CreateDirectory(settings.TileDirectory);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}
