using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MapCorleon.Contracts;
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
    private readonly IImportTaskService importTasks;
    private readonly IPlayerLocationCoordinator playerLocationCoordinator;
    private readonly IReadOnlyList<IGameLauncher> gameLaunchers;
    private readonly DispatcherTimer searchTimer;

    private List<Location> allLocations = [];
    private (int Picked, int Total) trackedProgress;
    private IReadOnlyList<CategoryGroupViewModel> categoryGroups = [];
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
        ISettingsService settingsService,
        IImportTaskService importTasks,
        IPlayerLocationCoordinator playerLocationCoordinator,
        IEnumerable<IGameLauncher> gameLaunchers)
    {
        this.db = db;
        this.locationService = locationService;
        this.filterService = filterService;
        this.searchService = searchService;
        this.eventBus = eventBus;
        this.settingsService = settingsService;
        this.importTasks = importTasks;
        this.playerLocationCoordinator = playerLocationCoordinator;
        this.gameLaunchers = gameLaunchers.ToList();
        importTasks.MediaDownloaded += MediaDownloaded;
        playerLocationCoordinator.PropertyChanged += PlayerLocationCoordinatorChanged;

        markFilter = settingsService.Current.Filter;
        searchTimer = new DispatcherTimer { Interval = SearchTiming.DebounceDelay };
        searchTimer.Tick += (_, _) =>
        {
            searchTimer.Stop();
            RefreshCategoryGroups();
            RefreshLocations();
        };

        ShowAllCommand = new RelayCommand(_ => MarkFilter = MarkFilter.All);
        ShowMarkedCommand = new RelayCommand(_ => MarkFilter = MarkFilter.Marked);
        ShowUnmarkedCommand = new RelayCommand(_ => MarkFilter = MarkFilter.Unmarked);
        SelectCommand = new RelayCommand(location => SelectedLocation = location as Location);
        ToggleMarkCommand = new AsyncCommand(ToggleMarkAsync, () => SelectedLocation is not null);
        ReloadCommand = new AsyncCommand(() => LoadAsync(currentMapName));
        LaunchGameCommand = new RelayCommand(_ => LaunchGame(), _ => CanLaunchGame);
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
    public IReadOnlyList<CategoryGroupViewModel> CategoryGroups
    {
        get => categoryGroups;
        private set => Set(ref categoryGroups, value);
    }
    public ObservableCollection<Map> Maps { get; } = [];
    public ObservableCollection<Timeline> Timelines { get; } = [];

    public ICommand ShowAllCommand { get; }
    public ICommand ShowMarkedCommand { get; }
    public ICommand ShowUnmarkedCommand { get; }
    public ICommand SelectCommand { get; }
    public ICommand ToggleMarkCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand LaunchGameCommand { get; }
    public ICommand ToggleCategoryCommand { get; }
    public ICommand ToggleGroupCommand { get; }

    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value)) return;
            OnPropertyChanged(nameof(IsSearchActive));
            searchTimer.Stop();
            if (IsSearchActive) searchTimer.Start();
            else
            {
                RefreshCategoryGroups();
                RefreshLocations();
            }
        }
    }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(Query);
    public string? CurrentMapName => currentMapName;
    public bool IsAllFilter => MarkFilter == MarkFilter.All;
    public bool IsMarkedFilter => MarkFilter == MarkFilter.Marked;
    public bool IsUnmarkedFilter => MarkFilter == MarkFilter.Unmarked;
    public string MarkActionText => SelectedLocation?.Marks.Count > 0 ? "Unmark" : "Mark";
    public int PickedCount => allLocations.Count(location => location.Marks.Count > 0);
    public int TotalCount => allLocations.Count;
    public string ProgressText => $"{trackedProgress.Picked} locations picked from {trackedProgress.Total} total";
    public string ProgressSummary => $"{trackedProgress.Picked:N0} of {trackedProgress.Total:N0} marked";
    public int ProgressPercentage => trackedProgress.Total == 0 ? 0 : (int)Math.Round(trackedProgress.Picked * 100d / trackedProgress.Total);
    public IReadOnlySet<int> ExcludedProgressCategoryIds => settingsService.Current.ExcludedProgressCategoryIds;
    public PlayerLocation? LivePlayerLocation => playerLocationCoordinator.CurrentLocation;
    public string PlayerTrackingStatusText => playerLocationCoordinator.StatusText;
    public bool IsPlayerTrackingSupported => CurrentMap is not null && playerLocationCoordinator.Supports(CurrentMap);
    public bool CanLaunchGame => IsPlayerTrackingSupported && CurrentGameLauncher is not null;

    private IGameLauncher? CurrentGameLauncher => CurrentMap is null
        ? null
        : gameLaunchers.FirstOrDefault(launcher => launcher.Supports(GameMapDescriptorFactory.Create(CurrentMap)));

    public bool IsLoading
    {
        get => isLoading;
        set => Set(ref isLoading, value);
    }

    public Map? CurrentMap
    {
        get => currentMap;
        private set
        {
            if (!Set(ref currentMap, value)) return;
            OnPropertyChanged(nameof(IsPlayerTrackingSupported));
            OnPropertyChanged(nameof(CanLaunchGame));
            ((RelayCommand)LaunchGameCommand).RaiseCanExecuteChanged();
        }
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
            if (Set(ref selectedTimeline, value) && value is not null) PersistSelectionAsync();
        }
    }

    public Location? SelectedLocation
    {
        get => selectedLocation;
        set
        {
            if (!Set(ref selectedLocation, value)) return;
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
            if (!Set(ref markFilter, value)) return;
            OnPropertyChanged(nameof(IsAllFilter));
            OnPropertyChanged(nameof(IsMarkedFilter));
            OnPropertyChanged(nameof(IsUnmarkedFilter));
            PersistFilterAsync();
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
            {
                await playerLocationCoordinator.SetActiveMapAsync(null);
                return;
            }

            var wantedMap = preferredMapName ?? settingsService.Current.CurrentMapName;
            var map = maps.FirstOrDefault(item => item.Name == wantedMap) ?? maps[0];

            currentMapName = map.Name;
            OnPropertyChanged(nameof(CurrentMapName));
            CurrentMap = map;
            CurrentMapConfig = map.Config;
            await playerLocationCoordinator.SetActiveMapAsync(map);

            await LoadGroupsAsync(map.Id);
            await LoadTimelinesAsync(map.Id);

            allLocations = await locationService.LoadAsync(map.Id, SelectedTimeline?.Id ?? 0);
            UpdateCategoryProgress();
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
        if (CurrentMap is null) return;
        SelectedTimeline = Timelines.FirstOrDefault(timeline => timeline.Id == timelineId);
        if (SelectedTimeline is null) return;
        allLocations = await locationService.LoadAsync(CurrentMap.Id, SelectedTimeline.Id);
        UpdateCategoryProgress();
        SelectedLocation = null;
        RefreshLocations();
    }

    public void SelectLocationById(int id) => SelectedLocation = allLocations.FirstOrDefault(location => location.Id == id);

    public async Task SaveProgressCategoriesAsync(IReadOnlyDictionary<int, bool> selections)
    {
        var settings = settingsService.Current;
        var excludedCategoryIds = new HashSet<int>(settings.ExcludedProgressCategoryIds);
        foreach (var (categoryId, included) in selections)
        {
            if (included) excludedCategoryIds.Remove(categoryId);
            else excludedCategoryIds.Add(categoryId);
        }

        await settingsService.SaveAsync(settings with { ExcludedProgressCategoryIds = excludedCategoryIds });
        UpdateTrackedProgress();
    }

    public void FocusSelectedLocation()
    {
        if (SelectedLocation is not null) OnPropertyChanged(nameof(SelectedLocation));
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
        RefreshCategoryGroups();
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
        if (SelectedLocation is null || SelectedTimeline is null || CurrentMap is null) return;
        var selectedId = SelectedLocation.Id;
        await locationService.ToggleMarkedAsync(selectedId, SelectedTimeline.Id);
        allLocations = await locationService.LoadAsync(CurrentMap.Id, SelectedTimeline.Id);
        UpdateCategoryProgress();
        SelectedLocation = allLocations.FirstOrDefault(location => location.Id == selectedId);
        RefreshLocations();
    }

    private void RefreshLocations()
    {
        var filtered = filterService.Apply(allLocations, MarkFilter, settingsService.Current.CategoryVisibility);
        var visible = searchService.Search(filtered, Query).ToList();

        ReplaceCollection(VisibleLocations, visible);
        ReplaceCollection(SearchResults, IsSearchActive ? visible : []);

        OnPropertyChanged(nameof(PickedCount));
        OnPropertyChanged(nameof(TotalCount));
    }

    private void UpdateTrackedProgress()
    {
        var excluded = ExcludedProgressCategoryIds;
        var picked = 0;
        var total = 0;
        foreach (var location in allLocations)
        {
            if (excluded.Contains(location.CategoryId)) continue;
            total++;
            if (location.Marks.Count > 0) picked++;
        }

        trackedProgress = (picked, total);
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressSummary));
        OnPropertyChanged(nameof(ProgressPercentage));
    }

    private void SetCategoryVisibility(Category category, bool visible)
    {
        category.IsVisible = visible;
        var values = new Dictionary<int, bool>(settingsService.Current.CategoryVisibility)
        {
            [category.Id] = visible
        };

        settingsService.SaveAsync(settingsService.Current with { CategoryVisibility = values });
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

        settingsService.SaveAsync(settingsService.Current with { CategoryVisibility = values });
        ReplaceCollection(Groups, Groups.ToList());
        RefreshLocations();
    }

    private Task PersistFilterAsync() => settingsService.SaveAsync(settingsService.Current with { Filter = MarkFilter });

    private void MediaDownloaded(object? sender, EventArgs e) =>
        Application.Current.Dispatcher.InvokeAsync(ReloadMediaAsync);

    private void PlayerLocationCoordinatorChanged(object? sender, PropertyChangedEventArgs e) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (e.PropertyName == nameof(IPlayerLocationCoordinator.CurrentLocation))
                OnPropertyChanged(nameof(LivePlayerLocation));
            else if (e.PropertyName == nameof(IPlayerLocationCoordinator.StatusText))
                OnPropertyChanged(nameof(PlayerTrackingStatusText));
        });

    private void LaunchGame()
    {
        var launcher = CurrentGameLauncher;
        if (launcher is null) return;
        var config = CurrentMapConfig;
        var launchSettings = config is null
            ? null
            : new GameLaunchSettings(config.LaunchExecutablePath, config.LaunchArguments, config.LaunchWorkingDirectory);
        var result = launcher.Launch(launchSettings);
        if (result.Status == GameLaunchStatus.Started)
            playerLocationCoordinator.NotifyGameStarting();
        else if (result.Status != GameLaunchStatus.AlreadyRunning)
            MessageBox.Show(result.Message, "Launch game", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RefreshCategoryGroups() => CategoryGroups = CategoryGroupFilter.Apply(Groups, Query);

    private async Task ReloadMediaAsync()
    {
        if (CurrentMap is null || SelectedTimeline is null) return;
        var selectedId = SelectedLocation?.Id;
        allLocations = await locationService.LoadAsync(CurrentMap.Id, SelectedTimeline.Id);
        UpdateCategoryProgress();
        SelectedLocation = selectedId.HasValue
            ? allLocations.FirstOrDefault(location => location.Id == selectedId.Value)
            : null;
        RefreshLocations();
    }

    private void UpdateCategoryProgress()
    {
        UpdateTrackedProgress();
        var counts = allLocations
            .GroupBy(location => location.CategoryId)
            .ToDictionary(
                group => group.Key,
                group => (Total: group.Count(), Picked: group.Count(location => location.Marks.Count > 0)));
        foreach (var category in Groups.SelectMany(group => group.Categories))
        {
            var progress = counts.GetValueOrDefault(category.Id);
            category.SetProgress(progress.Total, progress.Picked);
        }
    }

    private Task PersistSelectionAsync() => settingsService.SaveAsync(settingsService.Current with
    {
        CurrentMapName = currentMapName,
        CurrentTimelineId = selectedTimeline?.Id
    });

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        var snapshot = values.ToList();
        target.Clear();
        foreach (var value in snapshot) target.Add(value);
    }
}
