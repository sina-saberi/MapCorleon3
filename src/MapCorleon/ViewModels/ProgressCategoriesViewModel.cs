using MapCorleon.Models;
using System.Windows.Threading;

namespace MapCorleon.ViewModels;

public sealed class ProgressCategoriesViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyList<Group> groups;
    private readonly DispatcherTimer searchTimer;
    private IReadOnlyList<CategoryGroupViewModel> categoryGroups = [];
    private string query = string.Empty;

    public ProgressCategoriesViewModel(IEnumerable<Group> groups)
    {
        this.groups = groups.ToList();
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

    public IReadOnlyList<CategoryGroupViewModel> CategoryGroups
    {
        get => categoryGroups;
        private set => Set(ref categoryGroups, value);
    }

    private void RefreshGroups() => CategoryGroups = CategoryGroupFilter.Apply(groups, Query);

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
}
