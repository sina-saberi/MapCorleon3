using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MapCorleon.Contracts;
using MapCorleon.Data;
using MapCorleon.Models;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Services;

public interface IImportTaskService : INotifyPropertyChanged
{
    bool IsRunning { get; }
    bool CanPause { get; }
    string Status { get; }
    string? Error { get; }
    int Percentage { get; }
    event EventHandler? MediaDownloaded;
    event EventHandler? TasksChanged;
    Task InitializeAsync();
    Task<IReadOnlyList<DownloadTask>> GetTasksAsync();
    bool TryStart(IMapProvider provider, ImportResult result, bool downloadMedia, bool downloadTiles);
    Task PauseAsync(int taskId);
    Task ResumeAsync(int taskId);
}

public sealed class ImportTaskService(
    IDbContextFactory<AppDbContext> factory,
    IProviderStorage storage,
    ISettingsService settings,
    IMapProviderResolver providers) : IImportTaskService
{
    private const string Pending = "Pending", Running = "Running", Paused = "Paused", Failed = "Failed", Completed = "Completed";
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private CancellationTokenSource? cancellation;
    private Task? runningOperation;
    private int? activeTaskId;
    private bool isRunning;
    private string status = string.Empty;
    private string? error;
    private int percentage;

    public bool IsRunning { get => isRunning; private set { if (Set(ref isRunning, value)) OnPropertyChanged(nameof(CanPause)); } }
    public bool CanPause => IsRunning && activeTaskId is not null;
    public string Status { get => status; private set => Set(ref status, value); }
    public string? Error { get => error; private set => Set(ref error, value); }
    public int Percentage { get => percentage; private set => Set(ref percentage, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? MediaDownloaded;
    public event EventHandler? TasksChanged;

    public async Task InitializeAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var interrupted = await db.DownloadTasks.Where(x => x.Status == Running || x.Status == Pending).ToListAsync();
        foreach (var task in interrupted)
        {
            task.Status = Paused;
            task.Error = "The application stopped before this task finished.";
            task.UpdatedAtUtc = DateTime.UtcNow;
        }
        if (interrupted.Count > 0) await db.SaveChangesAsync();
        var latest = await db.DownloadTasks.Include(x => x.Map).ThenInclude(x => x!.Game)
            .OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefaultAsync();
        if (latest is not null) Apply(latest);
    }

    public async Task<IReadOnlyList<DownloadTask>> GetTasksAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DownloadTasks.AsNoTracking().Include(x => x.Map).ThenInclude(x => x!.Game)
            .OrderByDescending(x => x.UpdatedAtUtc).ToListAsync();
    }

    public bool TryStart(IMapProvider provider, ImportResult result, bool downloadMedia, bool downloadTiles)
    {
        if (!downloadMedia && !downloadTiles) return false;
        Status = "Creating offline download task";
        _ = CreateAndSwitchAsync(provider, result, downloadMedia, downloadTiles);
        return true;
    }

    public async Task PauseAsync(int taskId)
    {
        Task? operation = null;
        await switchLock.WaitAsync();
        try
        {
            if (activeTaskId == taskId && runningOperation is { IsCompleted: false })
            {
                cancellation?.Cancel();
                operation = runningOperation;
            }
        }
        finally { switchLock.Release(); }
        if (operation is not null) await operation;
    }

    public async Task ResumeAsync(int taskId)
    {
        await switchLock.WaitAsync();
        try
        {
            await StopActiveAsync(taskId);
            await using var db = await factory.CreateDbContextAsync();
            var task = await db.DownloadTasks.FindAsync(taskId);
            if (task is null || task.Status == Completed) return;
            var provider = providers.All.FirstOrDefault(x => x.Key == task.Provider);
            if (provider is null)
            {
                task.Status = Failed;
                task.Error = $"Download provider '{task.Provider}' is not available.";
                task.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await LoadTaskAsync(task.Id);
                RaiseTasksChanged();
                return;
            }
            StartOperation(provider, task.Id);
        }
        finally { switchLock.Release(); }
    }

    private async Task CreateAndSwitchAsync(IMapProvider provider, ImportResult result, bool downloadMedia, bool downloadTiles)
    {
        await switchLock.WaitAsync();
        try
        {
            await StopActiveAsync();
            await using var db = await factory.CreateDbContextAsync();
            var map = await db.Maps.FirstAsync(x => x.ExternalId == result.MapExternalId);
            var task = new DownloadTask
            {
                MapId = map.Id, Provider = provider.Key, DownloadMedia = downloadMedia, DownloadTiles = downloadTiles,
                Status = Pending, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };
            db.DownloadTasks.Add(task);
            await db.SaveChangesAsync();
            RaiseTasksChanged();
            StartOperation(provider, task.Id);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Status = $"Could not create offline download task: {ex.Message}";
        }
        finally { switchLock.Release(); }
    }

    private async Task StopActiveAsync(int? requestedTaskId = null)
    {
        if (runningOperation is not { IsCompleted: false } operation || activeTaskId == requestedTaskId) return;
        cancellation?.Cancel();
        await operation;
    }

    private void StartOperation(IMapProvider provider, int taskId)
    {
        activeTaskId = taskId;
        cancellation = new CancellationTokenSource();
        runningOperation = RunAsync(provider, taskId, cancellation.Token);
    }

    private async Task RunAsync(IMapProvider provider, int taskId, CancellationToken ct)
    {
        IsRunning = true;
        Error = null;
        try
        {
            await UpdateTaskAsync(taskId, Running, null, ct: CancellationToken.None);
            await using var db = await factory.CreateDbContextAsync(ct);
            var task = await db.DownloadTasks.AsNoTracking().FirstAsync(x => x.Id == taskId, ct);
            var plan = await BuildPlanAsync(db, task, ct);
            var completed = plan.AlreadyComplete;
            var failed = 0;
            await UpdateProgressAsync(taskId, completed, plan.Total, 0, "Preparing offline download", ct);

            foreach (var media in plan.Media)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var asset = await storage.CacheAsync(task.Provider, plan.MediaStorageKind, media.RelativePath,
                        Path.GetFileName(new Uri(media.RelativePath).AbsolutePath), ct);
                    await using var updateDb = await factory.CreateDbContextAsync(ct);
                    var item = await updateDb.Media.FindAsync([media.Id], ct);
                    if (item is not null)
                    {
                        item.RelativePath = asset.FullPath;
                        item.ContentHash = asset.ContentHash;
                        await updateDb.SaveChangesAsync(ct);
                    }
                    completed++;
                    MediaDownloaded?.Invoke(this, EventArgs.Empty);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failed++; Error = $"Media download failed: {ex.Message}"; }
                await UpdateProgressAsync(taskId, completed, plan.Total, failed,
                    $"Downloading media: {completed} complete, {failed} failed, {plan.Total} total", ct);
            }

            foreach (var tile in plan.Tiles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var bytes = await provider.DownloadTileAsync(tile.Url, ct);
                    if (bytes is null) throw new HttpRequestException("The server returned no tile data.");
                    Directory.CreateDirectory(Path.GetDirectoryName(tile.Path)!);
                    await File.WriteAllBytesAsync(tile.Path, bytes, ct);
                    completed++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failed++; Error = $"Tile download failed: {ex.Message}"; }
                await UpdateProgressAsync(taskId, completed, plan.Total, failed,
                    $"Downloading tiles: {completed} complete, {failed} failed, {plan.Total} total", ct);
            }

            if (failed > 0)
                await UpdateTaskAsync(taskId, Failed, $"{failed} item(s) could not be downloaded. Retry to attempt only missing items.", failed);
            else
                await UpdateTaskAsync(taskId, Completed, null);
        }
        catch (OperationCanceledException)
        {
            await UpdateTaskAsync(taskId, Paused, null);
        }
        catch (Exception ex)
        {
            await UpdateTaskAsync(taskId, Failed, ex.Message);
        }
        finally
        {
            if (activeTaskId == taskId)
            {
                activeTaskId = null;
                cancellation?.Dispose();
                cancellation = null;
                IsRunning = false;
                OnPropertyChanged(nameof(CanPause));
            }
            await LoadTaskAsync(taskId);
            RaiseTasksChanged();
        }
    }

    private async Task<DownloadPlan> BuildPlanAsync(AppDbContext db, DownloadTask task, CancellationToken ct)
    {
        var map = await db.Maps.AsNoTracking().FirstAsync(x => x.Id == task.MapId, ct);
        var allMedia = task.DownloadMedia
            ? await db.Media.AsNoTracking().Where(x => x.Location!.Category!.Group!.MapId == task.MapId).ToListAsync(ct)
            : [];
        var media = allMedia.Where(x => IsRemote(x.RelativePath)).ToList();
        var alreadyComplete = allMedia.Count(x => !IsRemote(x.RelativePath) && File.Exists(x.RelativePath));
        var total = media.Count + alreadyComplete;
        var tiles = new List<TileWork>();
        if (task.DownloadTiles)
        {
            var config = await db.MapConfigs.AsNoTracking().FirstAsync(x => x.MapId == task.MapId, ct);
            if (!string.IsNullOrWhiteSpace(config.TilePattern) && config.MinZoom is not null && config.MaxZoom is not null)
            {
                var exact = string.IsNullOrWhiteSpace(config.TileBoundsJson) ? null : JsonSerializer.Deserialize<Dictionary<int, TileRange>>(config.TileBoundsJson);
                var cache = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(config.TilePattern)))[..16];
                for (var z = config.MinZoom.Value; z <= config.MaxZoom.Value; z++)
                {
                    var range = exact is not null && exact.TryGetValue(z, out var b)
                        ? (b.MinX, b.MaxX, b.MinY, b.MaxY)
                        : GetRange(config.MinLatitude, config.MaxLatitude, config.MinLongitude, config.MaxLongitude, z);
                    for (var x = range.Item1; x <= range.Item2; x++)
                    for (var y = range.Item3; y <= range.Item4; y++)
                    {
                        var path = Path.Combine(settings.Current.TileDirectory, "mapsui", cache, z.ToString(), x.ToString(), $"{y}.tile");
                        total++;
                        if (File.Exists(path)) alreadyComplete++;
                        else tiles.Add(new(config.TilePattern.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString()), path));
                    }
                }
            }
        }
        return new(media, tiles, alreadyComplete, total, Path.Combine("media", $"game-{map.GameId}", $"map-{map.Id}"));
    }

    private async Task UpdateProgressAsync(int id, int completed, int total, int failed, string text, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var task = await db.DownloadTasks.FindAsync([id], ct);
        if (task is null) return;
        task.CompletedItems = completed;
        task.TotalItems = total;
        task.FailedItems = failed;
        task.Error = Error;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        Status = text;
        Percentage = total == 0 ? 100 : (int)Math.Round(completed * 100d / total);
        RaiseTasksChanged();
    }

    private async Task UpdateTaskAsync(int id, string state, string? taskError, int? failed = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var task = await db.DownloadTasks.Include(x => x.Map).ThenInclude(x => x!.Game).FirstAsync(x => x.Id == id, ct);
        task.Status = state;
        task.Error = taskError;
        if (failed is not null) task.FailedItems = failed.Value;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        Apply(task);
        RaiseTasksChanged();
    }

    private async Task LoadTaskAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var task = await db.DownloadTasks.Include(x => x.Map).ThenInclude(x => x!.Game).FirstOrDefaultAsync(x => x.Id == id);
        if (task is not null) Apply(task);
    }

    private void Apply(DownloadTask task)
    {
        Error = task.Error;
        Percentage = task.Percentage;
        Status = task.Status switch
        {
            Completed => $"{task.MapDisplayName}: offline download complete",
            Paused => $"{task.MapDisplayName}: paused at {task.Percentage}%",
            Failed => $"{task.MapDisplayName}: failed at {task.Percentage}% — {task.Error}",
            _ => $"{task.MapDisplayName}: downloading ({task.Percentage}%)"
        };
    }

    private void RaiseTasksChanged() => TasksChanged?.Invoke(this, EventArgs.Empty);
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name!);
        return true;
    }
    private static bool IsRemote(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    private static (int, int, int, int) GetRange(double minLat, double maxLat, double minLon, double maxLon, int zoom)
    {
        var a = ToTile(minLat, minLon, zoom); var b = ToTile(maxLat, maxLon, zoom);
        return (Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
    }
    private static (int X, int Y) ToTile(double latitude, double longitude, int zoom)
    {
        latitude = Math.Clamp(latitude, -85.05112878, 85.05112878);
        var scale = 1 << zoom;
        var x = (int)Math.Floor((longitude + 180) / 360 * scale);
        var radians = latitude * Math.PI / 180;
        var y = (int)Math.Floor((1 - Math.Log(Math.Tan(radians) + 1 / Math.Cos(radians)) / Math.PI) / 2 * scale);
        return (Math.Clamp(x, 0, scale - 1), Math.Clamp(y, 0, scale - 1));
    }
    private sealed record TileWork(string Url, string Path);
    private sealed record DownloadPlan(
        IReadOnlyList<Media> Media,
        IReadOnlyList<TileWork> Tiles,
        int AlreadyComplete,
        int Total,
        string MediaStorageKind);
}
