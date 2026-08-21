namespace MapCorleon.Models;

public sealed class DownloadTask : IEntity
{
    public int Id { get; set; }
    public int MapId { get; set; }
    public required string Provider { get; set; }
    public string? SourcePageUrl { get; set; }
    public bool DownloadMedia { get; set; }
    public bool DownloadTiles { get; set; }
    public required string Status { get; set; }
    public int CompletedItems { get; set; }
    public int TotalItems { get; set; }
    public int FailedItems { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Map? Map { get; set; }

    public int Percentage => TotalItems == 0 ? (Status == "Completed" ? 100 : 0)
        : (int)Math.Round(CompletedItems * 100d / TotalItems);
    public string MapDisplayName => Map?.DisplayName ?? "Unknown map";
    public string DownloadKinds => DownloadMedia && DownloadTiles ? "Media and tiles"
        : DownloadMedia ? "Media" : "Tiles";
    public string ProgressText => $"{CompletedItems:N0} of {TotalItems:N0} complete"
        + (FailedItems > 0 ? $" · {FailedItems:N0} failed" : string.Empty);
    public bool CanAct => Status != "Completed";
    public string ActionText => Status == "Running" ? "Pause"
        : Status == "Failed" ? "Retry"
        : Status == "Pending" ? "Start" : "Resume";
}
