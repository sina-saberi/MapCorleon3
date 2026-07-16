using MapCorleon.Models;

namespace MapCorleon.Services;

public interface IFilterService
{
    IEnumerable<Location> Apply(IEnumerable<Location> source, MarkFilter filter, IReadOnlyDictionary<int, bool> visibility);
}

public sealed class FilterService : IFilterService
{
    public IEnumerable<Location> Apply(IEnumerable<Location> source, MarkFilter filter, IReadOnlyDictionary<int, bool> visibility)
    {
        return source.Where(location => IsCategoryVisible(location, visibility) && MatchesMarkFilter(location, filter));
    }

    private static bool IsCategoryVisible(Location location, IReadOnlyDictionary<int, bool> visibility) => !visibility.TryGetValue(location.CategoryId, out var visible) || visible;

    private static bool MatchesMarkFilter(Location location, MarkFilter filter) => filter switch
    {
        MarkFilter.Marked => location.Marks.Count > 0,
        MarkFilter.Unmarked => location.Marks.Count == 0,
        _ => true
    };
}

public interface ISearchService
{
    IEnumerable<Location> Search(IEnumerable<Location> source, string? query);
}

public sealed class SearchService : ISearchService
{
    public IEnumerable<Location> Search(IEnumerable<Location> source, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return source;

        return source.Where(location =>
            location.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            location.DescriptionMarkdown.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            location.Category?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
            location.Category?.Group?.Name.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    }
}
