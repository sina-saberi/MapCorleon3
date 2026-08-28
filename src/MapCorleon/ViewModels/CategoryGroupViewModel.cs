using MapCorleon.Models;

namespace MapCorleon.ViewModels;

public sealed class CategoryGroupViewModel
{
    public CategoryGroupViewModel(Group group, IReadOnlyList<Category> categories)
    {
        Group = group;
        Categories = categories;
    }

    public Group Group { get; }
    public string Name => Group.Name;
    public IReadOnlyList<Category> Categories { get; }
}

public static class CategoryGroupFilter
{
    public static IReadOnlyList<CategoryGroupViewModel> Apply(IEnumerable<Group> groups, string? query)
    {
        var term = query?.Trim();
        var hasQuery = !string.IsNullOrEmpty(term);
        var results = new List<CategoryGroupViewModel>();

        foreach (var group in groups)
        {
            var groupMatches = hasQuery && group.Name.Contains(term!, StringComparison.OrdinalIgnoreCase);
            var categories = group.Categories
                .Where(category => !hasQuery || groupMatches || category.Name.Contains(term!, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (categories.Count > 0)
                results.Add(new CategoryGroupViewModel(group, categories));
        }

        return results;
    }
}
