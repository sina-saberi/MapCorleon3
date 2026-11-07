using System.IO;
using System.Text.Json;
using MapCorleon.Contracts;

namespace MapCorleon.Services;

internal sealed class BundledMapCatalog(EfMapImportSink sink, IAppLogService logs)
{
    internal static string Root => Path.Combine(AppContext.BaseDirectory, "Catalog");

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(Root)) return;
        foreach (var path in Directory.EnumerateFiles(Root, "*.map.json", SearchOption.AllDirectories).Order())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var entry = await JsonSerializer.DeserializeAsync<CatalogMap>(stream, cancellationToken: ct);
                if (entry is not { Revision: > 0, Map: { ExternalId: > 0 } } || string.IsNullOrWhiteSpace(entry.Game))
                    throw new InvalidDataException("The bundled map must have a game, a provider ID, and a positive revision.");
                await sink.UpsertCatalogAsync(entry, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await logs.WriteAsync("Catalog.Seed", "Error", $"Could not load {Path.GetFileName(path)}.", ex.ToString());
            }
        }
    }

    internal static Uri? Asset(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var path = Path.GetFullPath(Path.Combine(Root, relativePath));
        return path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
            ? new Uri(path) : null;
    }
}
