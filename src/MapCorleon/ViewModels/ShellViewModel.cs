using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using MapCorleon.Data;
using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly AppDbContext db;
    private readonly ILocationService locationService;
    private readonly IFilterService filterService;
    private readonly ISearchService searchService;
    private readonly IEventBus eventBus;
    private readonly ISettingsService settingsService;
    private readonly DispatcherTimer searchTimer;

    private List<Location> allLocations = [];
    private string query = string.Empty;
    private bool isLoading;
    private Location? selectedLocation;
    private MapConfig? currentMapConfig;
    private MarkFilter markFilter;
    private string? currentMapName;
    private Map? currentMap;
    private Timeline? selectedTimeline;

    public ShellViewModel(
        AppDbContext db,
        ILocationService locationService,
        IFilterService filterService,
        ISearchService searchService,
        IEventBus eventBus,
        ISettingsService settingsService)
    {
        this.db = db;
        this.locationService = locationService;
        this.filterService = filterService;
        this.searchService = searchService;
        this.eventBus = eventBus;
        this.settingsService = settingsService;

        markFilter = settingsService.Current.Filter;
        searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        searchTimer.Tick += (_, _) =>
        {
            searchTimer.Stop();
            RefreshLocations();
        };

        ShowAllCommand = new RelayCommand(_ => MarkFilter = MarkFilter.All);
        ShowMarkedCommand = new RelayCommand(_ => MarkFilter = MarkFilter.Marked);
        ShowUnmarkedCommand = new RelayCommand(_ => MarkFilter = MarkFilter.Unmarked);
        SelectCommand = new RelayCommand(location => SelectedLocation = location as Location);
        ToggleMarkCommand = new AsyncCommand(ToggleMarkAsync, () => SelectedLocation is not null);
        ReloadCommand = new AsyncCommand(() => LoadAsync(currentMapName));
        ToggleCategoryCommand = new RelayCommand(category =>
        {
            if (category is Category value)
                SetCategoryVisibility(value, value.IsVisible);
        });
        ToggleGroupCommand = new RelayCommand(group =>
        {
            if (group is Group value)
                ToggleGroupVisibility(value);
        });
    }

    public ObservableCollection<Location> VisibleLocations { get; } = [];
    public ObservableCollection<Location> SearchResults { get; } = [];
    public ObservableCollection<Group> Groups { get; } = [];
    public ObservableCollection<Map> Maps { get; } = [];
    public ObservableCollection<Timeline> Timelines { get; } = [];

    public ICommand ShowAllCommand { get; }
    public ICommand ShowMarkedCommand { get; }
    public ICommand ShowUnmarkedCommand { get; }
    public ICommand SelectCommand { get; }
    public ICommand ToggleMarkCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ToggleCategoryCommand { get; }
    public ICommand ToggleGroupCommand { get; }

    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value))
                return;

            OnPropertyChanged(nameof(IsSearchActive));
            searchTimer.Stop();
            if (IsSearchActive)
                searchTimer.Start();
            else
                RefreshLocations();
        }
    }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(Query);
    public string? CurrentMapName => currentMapName;
    public string MarkActionText => SelectedLocation?.Marks.Count > 0 ? "Unmark" : "Mark";
    public int PickedCount => allLocations.Count(location => location.Marks.Count > 0);
    public int TotalCount => allLocations.Count;
    public string ProgressText => $"{PickedCount} locations picked from {TotalCount} total";

    public bool IsLoading
    {
        get => isLoading;
        set => Set(ref isLoading, value);
    }

    public Map? CurrentMap
    {
        get => currentMap;
        private set => Set(ref currentMap, value);
    }

    public MapConfig? CurrentMapConfig
    {
        get => currentMapConfig;
        private set => Set(ref currentMapConfig, value);
    }

    public Timeline? SelectedTimeline
    {
        get => selectedTimeline;
        set
        {
            if (Set(ref selectedTimeline, value) && value is not null)
                _ = PersistSelectionAsync();
        }
    }

    public Location? SelectedLocation
    {
        get => selectedLocation;
        set
        {
            if (!Set(ref selectedLocation, value))
                return;

            eventBus.Select(value);
            ((AsyncCommand)ToggleMarkCommand).RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(MarkActionText));
        }
    }

    public MarkFilter MarkFilter
    {
        get => markFilter;
        set
        {
            if (!Set(ref markFilter, value))
                return;

            _ = PersistFilterAsync();
            RefreshLocations();
        }
    }

    public async Task LoadAsync(string? preferredMapName = null)
    {
        IsLoading = true;
        try
        {
            var maps = await db.Maps
                .AsNoTracking()
                .Include(map => map.Game)
                .Include(map => map.Config)
                .ToListAsync();

            ReplaceCollection(Maps, maps);
            if (maps.Count == 0)
                return;

            var wantedMap = preferredMapName ?? settingsService.Current.CurrentMapName;
            var map = maps.FirstOrDefault(item => item.Name == wantedMap) ?? maps[0];

            currentMapName = map.Name;
            OnPropertyChanged(nameof(CurrentMapName));
            CurrentMap = map;
            CurrentMapConfig = map.Config;

            await LoadGroupsAsync(map.Id);
            await LoadTimelinesAsync(map.Id);

            allLocations = await locationService.LoadAsync(map.Id, SelectedTimeline?.Id ?? 0);
            SelectedLocation = null;
            RefreshLocations();
            await PersistSelectionAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ChangeTimelineAsync(int timelineId)
    {
        if (CurrentMap is null)
            return;

        SelectedTimeline = Timelines.FirstOrDefault(timeline => timeline.Id == timelineId);
        if (SelectedTimeline is null)
            return;

        allLocations = await locationService.LoadAsync(CurrentMap.Id, SelectedTimeline.Id);
        SelectedLocation = null;
        RefreshLocations();
    }

    public void SelectLocationById(int id)
        => SelectedLocation = allLocations.FirstOrDefault(location => location.Id == id);

    public void FocusSelectedLocation()
    {
        if (SelectedLocation is not null)
            OnPropertyChanged(nameof(SelectedLocation));
    }

    private async Task LoadGroupsAsync(int mapId)
    {
        var groups = await db.Groups
            .AsNoTracking()
            .Include(group => group.Categories)
            .Where(group => group.MapId == mapId)
            .ToListAsync();

        foreach (var category in groups.SelectMany(group => group.Categories))
        {
            category.IsVisible =
                !settingsService.Current.CategoryVisibility.TryGetValue(category.Id, out var visible) ||
                visible;
        }

        ReplaceCollection(Groups, groups);
    }

    private async Task LoadTimelinesAsync(int mapId)
    {
        var timelines = await db.Timelines
            .AsNoTracking()
            .Where(timeline => timeline.MapId == mapId)
            .OrderBy(timeline => timeline.SortOrder)
            .ToListAsync();

        ReplaceCollection(Timelines, timelines);
        SelectedTimeline = timelines.FirstOrDefault(
            timeline => timeline.Id == settingsService.Current.CurrentTimelineId)
            ?? timelines.FirstOrDefault();
    }

    private async Task ToggleMarkAsync()
    {
        if (SelectedLocation is null || SelectedTimeline is null || CurrentMap is null)
            return;

        var selectedId = SelectedLocation.Id;
        await locationService.ToggleMarkedAsync(selectedId, SelectedTimeline.Id);
        allLocations = await locationService.LoadAsync(CurrentMap.Id, SelectedTimeline.Id);
        SelectedLocation = allLocations.FirstOrDefault(location => location.Id == selectedId);
        RefreshLocations();
    }

    private void RefreshLocations()
    {
        var filtered = filterService.Apply(
            allLocations,
            MarkFilter,
            settingsService.Current.CategoryVisibility);
        var visible = searchService.Search(filtered, Query).ToList();

        ReplaceCollection(VisibleLocations, visible);
        ReplaceCollection(SearchResults, IsSearchActive ? visible : []);

        OnPropertyChanged(nameof(PickedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ProgressText));
    }

    private void SetCategoryVisibility(Category category, bool visible)
    {
        category.IsVisible = visible;
        var values = new Dictionary<int, bool>(settingsService.Current.CategoryVisibility)
        {
            [category.Id] = visible
        };

        _ = settingsService.SaveAsync(settingsService.Current with { CategoryVisibility = values });
        RefreshLocations();
    }

    private void ToggleGroupVisibility(Group group)
    {
        var visible = !group.Categories.Any(category => category.IsVisible);
        var values = new Dictionary<int, bool>(settingsService.Current.CategoryVisibility);

        foreach (var category in group.Categories)
        {
            category.IsVisible = visible;
            values[category.Id] = visible;
        }

        _ = settingsService.SaveAsync(settingsService.Current with { CategoryVisibility = values });
        ReplaceCollection(Groups, Groups.ToList());
        RefreshLocations();
    }

    private Task PersistFilterAsync()
        => settingsService.SaveAsync(settingsService.Current with { Filter = MarkFilter });

    private Task PersistSelectionAsync()
        => settingsService.SaveAsync(settingsService.Current with
        {
            CurrentMapName = currentMapName,
            CurrentTimelineId = selectedTimeline?.Id
        });

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        var snapshot = values.ToList();
        target.Clear();
        foreach (var value in snapshot)
            target.Add(value);
    }
}
