namespace MapCorleon.Contracts;

public sealed class MapProviderResolver(IEnumerable<IMapProvider> providers) : IMapProviderResolver
{
    private readonly Dictionary<string, IMapProvider> providers = providers.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<IMapProvider> All => providers.Values.OrderBy(x => x.DisplayName).ToList();

    public IMapProvider Get(string key) => providers.TryGetValue(key, out var provider)
        ? provider
        : throw new KeyNotFoundException($"Map provider '{key}' is not registered.");
}
