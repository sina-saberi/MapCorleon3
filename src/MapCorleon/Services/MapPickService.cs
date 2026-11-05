namespace MapCorleon.Services;

public readonly record struct MapPosition(double Latitude, double Longitude);

public interface IMapPickService
{
    event EventHandler? PickingChanged;
    bool IsPicking { get; }
    Task<MapPosition> PickAsync(CancellationToken ct = default);
    bool Complete(MapPosition position);
    bool Cancel();
}

public sealed class MapPickService : IMapPickService
{
    private TaskCompletionSource<MapPosition>? pending;
    private CancellationTokenRegistration cancellationRegistration;

    public event EventHandler? PickingChanged;

    public bool IsPicking => pending is not null;

    public Task<MapPosition> PickAsync(CancellationToken ct = default)
    {
        if (pending is not null)
            throw new InvalidOperationException("A map position is already being selected.");

        var request = new TaskCompletionSource<MapPosition>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = request;
        if (ct.CanBeCanceled)
            cancellationRegistration = ct.Register(() => Cancel(request, ct));
        PickingChanged?.Invoke(this, EventArgs.Empty);
        return request.Task;
    }

    public bool Complete(MapPosition position)
    {
        var request = pending;
        if (request is null) return false;

        pending = null;
        cancellationRegistration.Dispose();
        PickingChanged?.Invoke(this, EventArgs.Empty);
        return request.TrySetResult(position);
    }

    public bool Cancel()
    {
        var request = pending;
        if (request is null) return false;

        pending = null;
        cancellationRegistration.Dispose();
        PickingChanged?.Invoke(this, EventArgs.Empty);
        return request.TrySetCanceled();
    }

    private void Cancel(TaskCompletionSource<MapPosition> request, CancellationToken ct)
    {
        if (!ReferenceEquals(pending, request)) return;

        pending = null;
        PickingChanged?.Invoke(this, EventArgs.Empty);
        request.TrySetCanceled(ct);
    }
}
