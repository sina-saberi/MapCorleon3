namespace MapCorleon.Services;

public interface IMapPickService
{
    bool IsPicking { get; }
    Task<(double Latitude, double Longitude)> PickAsync(CancellationToken ct = default);
    bool Complete(double latitude, double longitude);
}

public sealed class MapPickService : IMapPickService
{
    private TaskCompletionSource<(double, double)>? pending;
    public bool IsPicking => pending is not null;
    public Task<(double Latitude, double Longitude)> PickAsync(CancellationToken ct = default)
    {
        if (pending is not null) throw new InvalidOperationException("A map position is already being selected.");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => { pending?.TrySetCanceled(ct); pending = null; });
        return pending.Task;
    }
    public bool Complete(double latitude, double longitude)
    {
        var request = pending; if (request is null) return false; pending = null;
        return request.TrySetResult((latitude, longitude));
    }
}
