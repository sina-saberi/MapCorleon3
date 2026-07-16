using MapCorleon.Models;

namespace MapCorleon.Services;

public interface IEventBus
{
    event EventHandler<Location?>? SelectionChanged;
    void Select(Location? location);
}

public sealed class EventBus : IEventBus
{
    public event EventHandler<Location?>? SelectionChanged;

    public void Select(Location? location) => SelectionChanged?.Invoke(this, location);
}
