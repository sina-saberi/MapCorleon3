using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
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
    string ActiveTaskLabel { get; }
    string? Error { get; }
    int Percentage { get; }
    event EventHandler? MediaDownloaded;
    event EventHandler? TasksChanged;
    Task InitializeAsync();
    Task<IReadOnlyList<DownloadTask>> GetTasksAsync();
    Task<IReadOnlyList<Map>> GetDownloadableMapsAsync();
    Task<int> EnqueueAsync(int mapId, bool downloadMedia, bool downloadTiles);
    Task PauseAsync(int taskId);
    Task ResumeAsync(int taskId);
    Task CancelAsync(int taskId);
}

public sealed class ImportTaskService(
    IDbContextFactory<AppDbContext> factory,
    IMediaDownloadService mediaDownloads,
    ISettingsService settings,
    IMapProviderResolver providers,
    IAppLogService appLogs) : IImportTaskService
{
    private const string Pending = "Pending", Running = "Running", Paused = "Paused", Failed = "Failed", Completed = "Completed";
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private CancellationTokenSource? cancellation;
    private Task? runningOperation;
    private int? activeTaskId;
    private bool isRunning;
    private string status = string.Empty;
    private string activeTaskLabel = string.Empty;
    private string? error;
    private int percentage;
    private DateTime lastProgressPersistUtc = DateTime.MinValue;

    public bool IsRunning { get => isRunning; private set { if (Set(ref isRunning, value)) OnPropertyChanged(nameof(CanPause)); } }
    public bool CanPause => IsRunning && activeTaskId is not null;
    public string Status { get => status; private set => Set(ref status, value); }
    public string ActiveTaskLabel { get => activeTaskLabel; private set => Set(ref activeTaskLabel, value); }
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

    public async Task<IReadOnlyList<Map>> GetDownloadableMapsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Maps.AsNoTracking().Include(x => x.Game).Include(x => x.Config)
            .Where(x => x.Config != null && (x.Config.TilePattern != null || db.Media.Any(media =>
                media.ExternalId != null && media.Location!.Category!.Group!.MapId == x.Id)))
            .OrderBy(x => x.Game!.Name).ThenBy(x => x.Name).ToListAsync();
    }

    public async Task<int> EnqueueAsync(int mapId, bool downloadMedia, bool downloadTiles)
    {
        await using var db = await factory.CreateDbContextAsync();
        var map = await db.Maps.AsNoTracking().Include(x => x.Config).FirstOrDefaultAsync(x => x.Id == mapId)
            ?? throw new InvalidOperationException("The selected map no longer exists.");
        var provider = map.Config?.Provider;
        if (string.IsNullOrWhiteSpace(provider))
            throw new InvalidOperationException("The selected map has no download provider.");
        if (downloadTiles && (string.IsNullOrWhiteSpace(map.Config?.TilePattern) || TileCoverage.Create(map.Config) is null))
            throw new InvalidOperationException("The selected map has no downloadable tile coverage.");
        var sourcePageUrl = map.Config?.SourcePageUrl ?? await db.DownloadTasks.AsNoTracking()
            .Where(x => x.MapId == mapId && x.SourcePageUrl != null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.SourcePageUrl)
            .FirstOrDefaultAsync();
        return await EnqueueCoreAsync(mapId, provider, downloadMedia, downloadTiles, sourcePageUrl);
    }

    public Task PauseAsync(int taskId)
    {
        if (activeTaskId == taskId && runningOperation is { IsCompleted: false })
        {
            Status = "Pausing download…";
            cancellation?.Cancel();
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync(int taskId)
    {
        Status = "Switching download task…";
        _ = SwitchToTaskAsync(taskId);
        return Task.CompletedTask;
    }

    public Task CancelAsync(int taskId)
    {
        if (activeTaskId == taskId) Status = "Cancelling download task…";
        _ = CancelAndDeleteAsync(taskId);
        return Task.CompletedTask;
    }

    private async Task CancelAndDeleteAsync(int taskId)
    {
        await switchLock.WaitAsync();
        try
        {
            if (activeTaskId == taskId && runningOperation is { IsCompleted: false } operation)
            {
                cancellation?.Cancel();
                await operation.ConfigureAwait(false);
            }
            await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
            var task = await db.DownloadTasks.FindAsync(taskId).ConfigureAwait(false);
            if (task is not null)
            {
                db.DownloadTasks.Remove(task);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            RaiseTasksChanged();
            if (!IsRunning) { Status = string.Empty; Percentage = 0; Error = null; }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Status = $"Could not remove download task: {ex.Message}";
        }
        finally { switchLock.Release(); }
    }

    private async Task SwitchToTaskAsync(int taskId)
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
        catch (Exception ex)
        {
            Error = ex.Message;
            Status = $"Could not start download task: {ex.Message}";
        }
        finally { switchLock.Release(); }
    }

    private async Task<int> EnqueueCoreAsync(
        int mapId, string providerKey, bool downloadMedia, bool downloadTiles, string? sourcePageUrl)
    {
        if (!downloadMedia && !downloadTiles) return 0;
        await switchLock.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            if (!await db.Maps.AnyAsync(x => x.Id == mapId))
                throw new InvalidOperationException("The selected map no longer exists.");
            var provider = providers.All.FirstOrDefault(x => x.Key == providerKey)
                ?? throw new InvalidOperationException($"Download provider '{providerKey}' is not available.");

            var now = DateTime.UtcNow;
            var created = new List<DownloadTask>(2);
            if (downloadMedia) created.Add(CreateTask(mapId, providerKey, sourcePageUrl, downloadMedia: true, now));
            if (downloadTiles) created.Add(CreateTask(mapId, providerKey, sourcePageUrl, downloadMedia: false, now));
            db.DownloadTasks.AddRange(created);
            await db.SaveChangesAsync();
            Status = created.Count == 1 ? "Added offline download to queue" : "Added 2 offline downloads to queue";
            RaiseTasksChanged();

            if (activeTaskId is null)
                StartOperation(provider, created[0].Id);
            return created.Count;
        }
        finally { switchLock.Release(); }
    }

    private static DownloadTask CreateTask(
        int mapId, string provider, string? sourcePageUrl, bool downloadMedia, DateTime now) => new()
    {
        MapId = mapId,
        Provider = provider,
        SourcePageUrl = sourcePageUrl,
        DownloadMedia = downloadMedia,
        DownloadTiles = !downloadMedia,
        Status = Pending,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private async Task StopActiveAsync(int? requestedTaskId = null)
    {
        if (runningOperation is not { IsCompleted: false } operation || activeTaskId == requestedTaskId) return;
        cancellation?.Cancel();
        await operation;
    }

    private void StartOperation(IMapProvider provider, int taskId)
    {
        activeTaskId = taskId;
        var workerCancellation = new CancellationTokenSource();
        cancellation = workerCancellation;
        // The plan can contain hundreds of thousands of tiles. Always build and
        // execute it away from WPF's dispatcher thread.
        runningOperation = Task.Run(() => RunAsync(provider, taskId, workerCancellation.Token));
    }

    private async Task RunAsync(IMapProvider provider, int taskId, CancellationToken ct)
    {
        Error = null;
        var mediaChanged = false;
        try
        {
            await UpdateTaskAsync(taskId, Running, null, ct: CancellationToken.None);
            IsRunning = true;
            await using var db = await factory.CreateDbContextAsync(ct);
            var task = await db.DownloadTasks.AsNoTracking().FirstAsync(x => x.Id == taskId, ct);
            var plan = await BuildPlanAsync(db, task, ct);
            var completed = plan.AlreadyComplete;
            var failed = 0;
            await UpdateProgressAsync(taskId, completed, plan.Total, 0, "Preparing offline download", ct);

            foreach (var media in plan.Media)
            {
                ct.ThrowIfCancellationRequested();
                Trace.WriteLine($"[MapCorleon DownloadTask #{taskId}] Media URL: {media.RelativePath}");
                using var itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                itemCancellation.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    await mediaDownloads.EnsureDownloadedAsync(media.Id, task.Provider, plan.MediaStorageKind, task.SourcePageUrl, itemCancellation.Token)
                        .WaitAsync(itemCancellation.Token);
                    completed++;
                    mediaChanged = true;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    failed++;
                    Error = $"Media download timed out after 30 seconds: {media.SourceUrl ?? media.RelativePath}";
                    await appLogs.WriteAsync("Download.Media", "Warning", Error, $"TaskId: {taskId}\nMediaId: {media.Id}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    Error = $"Media download failed for {media.RelativePath}: {ex.Message}";
                    await appLogs.WriteAsync("Download.Media", "Error", Error,
                        $"TaskId: {taskId}\nMediaId: {media.Id}\n{ex}");
                }
                await UpdateProgressAsync(taskId, completed, plan.Total, failed,
                    $"Downloading media: {completed} complete, {failed} failed, {plan.Total} total", ct,
                    force: false);
            }

            foreach (var tile in plan.Tiles)
            {
                ct.ThrowIfCancellationRequested();
                Trace.WriteLine($"[MapCorleon DownloadTask #{taskId}] Tile GET {tile.Url}");
                using var itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                itemCancellation.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    var bytes = await provider.DownloadTileAsync(tile.Url, itemCancellation.Token)
                        .WaitAsync(itemCancellation.Token);
                    itemCancellation.Token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(tile.Path)!);
                    await File.WriteAllBytesAsync(tile.Path, bytes ?? TilePlaceholder.TransparentPng, ct);
                    Trace.WriteLine($"[MapCorleon DownloadTask #{taskId}] Tile saved {tile.Path}");
                    completed++;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    failed++;
                    Error = $"Tile download timed out after 30 seconds: {tile.Url}";
                    await appLogs.WriteAsync("Download.Tile", "Warning", Error, $"TaskId: {taskId}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed++;
                    Error = $"Tile download failed: {ex.Message}";
                    await appLogs.WriteAsync("Download.Tile", "Error", Error,
                        $"TaskId: {taskId}\nUrl: {tile.Url}\n{ex}");
                }
                await UpdateProgressAsync(taskId, completed, plan.Total, failed,
                    $"Downloading tiles: {completed} complete, {failed} failed, {plan.Total} total", ct,
                    force: false);
            }

            await UpdateProgressAsync(taskId, completed, plan.Total, failed,
                failed > 0 ? "Offline download finished with missing items" : "Offline download complete",
                CancellationToken.None, force: true);
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
            await appLogs.WriteAsync("Download.Task", "Error", ex.Message, $"TaskId: {taskId}\n{ex}");
        }
        finally
        {
            // Reloading the full location/media graph after every image blocks
            // WPF's dispatcher. Refresh once when this run yields instead.
            if (mediaChanged) MediaDownloaded?.Invoke(this, EventArgs.Empty);
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
            _ = StartNextPendingAsync();
        }
    }

    private async Task StartNextPendingAsync()
    {
        await switchLock.WaitAsync();
        try
        {
            if (activeTaskId is not null) return;
            await using var db = await factory.CreateDbContextAsync();
            var next = await db.DownloadTasks.AsNoTracking()
                .Where(x => x.Status == Pending)
                .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                .FirstOrDefaultAsync();
            if (next is null) return;
            var provider = providers.All.FirstOrDefault(x => x.Key == next.Provider);
            if (provider is null)
            {
                await UpdateTaskAsync(next.Id, Failed, $"Download provider '{next.Provider}' is not available.");
                return;
            }
            StartOperation(provider, next.Id);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Status = $"Could not start the next queued download: {ex.Message}";
        }
        finally { switchLock.Release(); }
    }

    private async Task<DownloadPlan> BuildPlanAsync(AppDbContext db, DownloadTask task, CancellationToken ct)
    {
        var map = await db.Maps.AsNoTracking().FirstAsync(x => x.Id == task.MapId, ct);
        var allMedia = task.DownloadMedia
            ? await db.Media.AsNoTracking().Where(x => x.Location!.Category!.Group!.MapId == task.MapId && x.ExternalId != null).ToListAsync(ct)
            : [];
        var media = allMedia.Where(x => IsRemote(x.RelativePath) || !File.Exists(x.RelativePath)).ToList();
        var alreadyComplete = allMedia.Count(x => !IsRemote(x.RelativePath) && File.Exists(x.RelativePath));
        var total = media.Count + alreadyComplete;
        var tiles = new List<TileWork>();
        if (task.DownloadTiles)
        {
            var config = await db.MapConfigs.AsNoTracking().FirstAsync(x => x.MapId == task.MapId, ct);
            var coverage = TileCoverage.Create(config);
            if (!string.IsNullOrWhiteSpace(config.TilePattern) && coverage is not null)
            {
                var cache = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(config.TilePattern)))[..16];
                foreach (var (z, range) in coverage.Ranges.OrderBy(x => x.Key))
                {
                    for (var x = range.MinX; x <= range.MaxX; x++)
                    for (var y = range.MinY; y <= range.MaxY; y++)
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

    private async Task UpdateProgressAsync(int id, int completed, int total, int failed, string text, CancellationToken ct, bool force = true)
    {
        Status = text;
        Percentage = total == 0 ? 100 : (int)Math.Round(completed * 100d / total);
        var now = DateTime.UtcNow;
        if (!force && now - lastProgressPersistUtc < TimeSpan.FromSeconds(1)) return;
        lastProgressPersistUtc = now;
        await using var db = await factory.CreateDbContextAsync(ct);
        var task = await db.DownloadTasks.FindAsync([id], ct);
        if (task is null) return;
        task.CompletedItems = completed;
        task.TotalItems = total;
        task.FailedItems = failed;
        task.Error = Error;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
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
        ActiveTaskLabel = $"{task.MapDisplayName} · {task.DownloadKinds}";
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
    private sealed record TileWork(string Url, string Path);
    private sealed record DownloadPlan(
        IReadOnlyList<Media> Media,
        IReadOnlyList<TileWork> Tiles,
        int AlreadyComplete,
        int Total,
        string MediaStorageKind);
}
