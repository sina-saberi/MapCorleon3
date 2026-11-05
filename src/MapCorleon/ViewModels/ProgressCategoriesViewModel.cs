using MapCorleon.Models;
using System.Windows.Threading;

namespace MapCorleon.ViewModels;

public sealed class ProgressCategoriesViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyList<Group> groups;
    private readonly Dictionary<int, CategoryChoice> choices;
    private readonly DispatcherTimer searchTimer;
    private IReadOnlyList<CategoryChoiceGroup> categoryGroups = [];
    private string query = string.Empty;

    public ProgressCategoriesViewModel(IEnumerable<Group> groups, IReadOnlySet<int> excludedCategoryIds)
    {
        this.groups = groups.ToList();
        choices = this.groups.SelectMany(group => group.Categories).ToDictionary(
            category => category.Id,
            category => new CategoryChoice(category, !excludedCategoryIds.Contains(category.Id)));
        searchTimer = new DispatcherTimer { Interval = SearchTiming.DebounceDelay };
        searchTimer.Tick += SearchTimerTick;
        RefreshGroups();
    }

    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value)) return;
            searchTimer.Stop();
            if (string.IsNullOrWhiteSpace(Query))
                RefreshGroups();
            else
                searchTimer.Start();
        }
    }

    public IReadOnlyList<CategoryChoiceGroup> CategoryGroups
    {
        get => categoryGroups;
        private set => Set(ref categoryGroups, value);
    }

    public IReadOnlyDictionary<int, bool> GetSelections() =>
        choices.ToDictionary(entry => entry.Key, entry => entry.Value.IsIncluded);

    private void RefreshGroups() => CategoryGroups = CategoryGroupFilter.Apply(groups, Query)
        .Select(group => new CategoryChoiceGroup(group.Name, group.Categories.Select(category => choices[category.Id]).ToList()))
        .ToList();

    private void SearchTimerTick(object? sender, EventArgs e)
    {
        searchTimer.Stop();
        RefreshGroups();
    }

    public void Dispose()
    {
        searchTimer.Stop();
        searchTimer.Tick -= SearchTimerTick;
    }

    public sealed record CategoryChoiceGroup(string Name, IReadOnlyList<CategoryChoice> Categories);

    public sealed class CategoryChoice(Category category, bool isIncluded)
    {
        public Category Category { get; } = category;
        public bool IsIncluded { get; set; } = isIncluded;
    }
}
