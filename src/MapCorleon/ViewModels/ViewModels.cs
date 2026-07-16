using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MapCorleon.Data;
using MapCorleon.Models;
using MapCorleon.Services;
using Microsoft.EntityFrameworkCore;
using System.Windows.Threading;
namespace MapCorleon.ViewModels;

public abstract class Vm : INotifyPropertyChanged { public event PropertyChangedEventHandler? PropertyChanged; protected bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; PropertyChanged?.Invoke(this, new(n)); return true; } protected void Changed(string n) => PropertyChanged?.Invoke(this, new(n)); }
public sealed class AsyncCommand(Func<Task> action, Func<bool>? can = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p) => can?.Invoke() ?? true; public async void Execute(object? p) => await action(); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
public sealed class RelayCommand(Action<object?> action, Predicate<object?>? can = null) : ICommand { public event EventHandler? CanExecuteChanged; public bool CanExecute(object? p) => can?.Invoke(p) ?? true; public void Execute(object? p) => action(p); public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
public sealed class ShellViewModel : Vm
{
    readonly AppDbContext db; readonly ILocationService locations; readonly IFilterService filter; readonly ISearchService search; readonly IEventBus bus; readonly ISettingsService settings; readonly DispatcherTimer searchTimer; List<Location> all = []; string query = ""; bool loading; Location? selected; MapConfig? currentMapConfig; MarkFilter markFilter; string? currentMapName; Map? currentMap; Timeline? selectedTimeline;
    public ObservableCollection<Location> VisibleLocations { get; } = []; public ObservableCollection<Location> SearchResults { get; } = []; public ObservableCollection<Group> Groups { get; } = []; public ObservableCollection<Map> Maps { get; } = []; public ObservableCollection<Timeline> Timelines { get; } = [];
    public string Query { get => query; set { if (!Set(ref query, value)) return; Changed(nameof(IsSearchActive)); searchTimer.Stop(); if (!IsSearchActive) Refresh(); else searchTimer.Start(); } }
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(Query);
    public string? CurrentMapName => currentMapName;
    public Map? CurrentMap { get => currentMap; private set => Set(ref currentMap, value); }
    public Timeline? SelectedTimeline { get => selectedTimeline; set { if (Set(ref selectedTimeline, value) && value is not null) _ = PersistSelectionAsync(); } }
    public string MarkActionText => SelectedLocation?.Marks.Count > 0 ? "Unmark" : "Mark";
    public bool IsLoading { get => loading; set => Set(ref loading, value); }
    public Location? SelectedLocation { get => selected; set { if (Set(ref selected, value)) { bus.Select(value); (ToggleMarkCommand as AsyncCommand)?.Raise(); Changed(nameof(MarkActionText)); } } }
    public MapConfig? CurrentMapConfig { get => currentMapConfig; private set => Set(ref currentMapConfig, value); }
    public MarkFilter MarkFilter { get => markFilter; set { if (Set(ref markFilter, value)) { _ = PersistFilterAsync(); Refresh(); } } }
    public int PickedCount => all.Count(x => x.Marks.Count > 0); public int TotalCount => all.Count; public string ProgressText => $"{PickedCount} locations picked from {TotalCount} total";
    public ICommand ShowAllCommand { get; }
    public ICommand ShowMarkedCommand { get; }
    public ICommand ShowUnmarkedCommand { get; }
    public ICommand SelectCommand { get; }
    public ICommand ToggleMarkCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ToggleCategoryCommand { get; }
    public ICommand ToggleGroupCommand { get; }
    public ShellViewModel(AppDbContext db, ILocationService locations, IFilterService filter, ISearchService search, IEventBus bus, ISettingsService settings) { this.db = db; this.locations = locations; this.filter = filter; this.search = search; this.bus = bus; this.settings = settings; markFilter = settings.Current.Filter; searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) }; searchTimer.Tick += (_, _) => { searchTimer.Stop(); Refresh(); }; ShowAllCommand = new RelayCommand(_ => MarkFilter = MarkFilter.All); ShowMarkedCommand = new RelayCommand(_ => MarkFilter = MarkFilter.Marked); ShowUnmarkedCommand = new RelayCommand(_ => MarkFilter = MarkFilter.Unmarked); SelectCommand = new RelayCommand(x => SelectedLocation = x as Location); ToggleMarkCommand = new AsyncCommand(ToggleMarkAsync, () => SelectedLocation is not null); ReloadCommand = new AsyncCommand(() => LoadAsync(currentMapName)); ToggleCategoryCommand = new RelayCommand(x => { if (x is Category c) SetCategory(c, c.IsVisible); }); ToggleGroupCommand = new RelayCommand(x => { if (x is Group g) SetGroup(g); }); }
    public async Task LoadAsync(string? preferredMapName = null) { IsLoading = true; try { var maps = await db.Maps.AsNoTracking().Include(x => x.Game).Include(x => x.Config).ToListAsync(); Maps.Clear(); foreach (var m in maps) Maps.Add(m); if (maps.Count == 0) return; var wantedMap = preferredMapName ?? settings.Current.CurrentMapName; var map = maps.FirstOrDefault(x => x.Name == wantedMap) ?? maps[0]; currentMapName = map.Name; Changed(nameof(CurrentMapName)); CurrentMap = map; CurrentMapConfig = map.Config; var groups = await db.Groups.AsNoTracking().Include(x => x.Categories).Where(x => x.MapId == map.Id).ToListAsync(); Groups.Clear(); foreach (var g in groups) { foreach (var c in g.Categories) c.IsVisible = !settings.Current.CategoryVisibility.TryGetValue(c.Id, out var visible) || visible; Groups.Add(g); } var tls = await db.Timelines.AsNoTracking().Where(x => x.MapId == map.Id).OrderBy(x => x.SortOrder).ToListAsync(); Timelines.Clear(); foreach (var t in tls) Timelines.Add(t); SelectedTimeline = tls.FirstOrDefault(x => x.Id == settings.Current.CurrentTimelineId) ?? tls.FirstOrDefault(); all = await locations.LoadAsync(map.Id, SelectedTimeline?.Id ?? 0); SelectedLocation = null; Refresh(); await PersistSelectionAsync(); } finally { IsLoading = false; } }
    async Task ToggleMarkAsync() { if (SelectedLocation is null || SelectedTimeline is null || CurrentMap is null) return; var id = SelectedLocation.Id; await locations.ToggleMarkedAsync(id,SelectedTimeline.Id); all = await locations.LoadAsync(CurrentMap.Id,SelectedTimeline.Id); SelectedLocation = all.FirstOrDefault(x => x.Id == id); Refresh(); }
    public async Task ChangeTimelineAsync(int timelineId) { if (CurrentMap is null) return; SelectedTimeline = Timelines.FirstOrDefault(x=>x.Id==timelineId); if(SelectedTimeline is null)return; all=await locations.LoadAsync(CurrentMap.Id,SelectedTimeline.Id); SelectedLocation=null; Refresh(); }
    void Refresh() { var filtered = filter.Apply(all, MarkFilter, settings.Current.CategoryVisibility); var visible = search.Search(filtered, Query).ToList(); VisibleLocations.Clear(); foreach (var l in visible) VisibleLocations.Add(l); SearchResults.Clear(); if (IsSearchActive) foreach (var l in visible) SearchResults.Add(l); Changed(nameof(PickedCount)); Changed(nameof(TotalCount)); Changed(nameof(ProgressText)); }
    void SetCategory(Category category, bool visible) { category.IsVisible = visible; var values = new Dictionary<int, bool>(settings.Current.CategoryVisibility) { [category.Id] = visible }; _ = settings.SaveAsync(settings.Current with { CategoryVisibility = values }); Refresh(); }
    void SetGroup(Group group) { var visible = !group.Categories.Any(x => x.IsVisible); var values = new Dictionary<int, bool>(settings.Current.CategoryVisibility); foreach (var category in group.Categories) { category.IsVisible = visible; values[category.Id] = visible; } _ = settings.SaveAsync(settings.Current with { CategoryVisibility = values }); var snapshot = Groups.ToList(); Groups.Clear(); foreach (var item in snapshot) Groups.Add(item); Refresh(); }
    public void SelectLocationById(int id) => SelectedLocation = all.FirstOrDefault(x => x.Id == id);
    public void FocusSelectedLocation() { if (SelectedLocation is not null) Changed(nameof(SelectedLocation)); }
    async Task PersistFilterAsync() => await settings.SaveAsync(settings.Current with { Filter = MarkFilter });
    async Task PersistSelectionAsync() => await settings.SaveAsync(settings.Current with { CurrentMapName = currentMapName, CurrentTimelineId = selectedTimeline?.Id });
}
