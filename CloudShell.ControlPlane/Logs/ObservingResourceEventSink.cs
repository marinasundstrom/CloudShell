using CloudShell.Abstractions.Logs;

namespace CloudShell.ControlPlane.Logs;

public sealed class ObservingResourceEventSink(
    IResourceEventStore store,
    IEnumerable<IResourceEventObserver> observers) : IResourceEventSink
{
    private readonly IReadOnlyList<IResourceEventObserver> observers = observers.ToArray();

    public void Append(ResourceEvent resourceEvent)
    {
        var enrichedEvent = resourceEvent.WithCurrentTraceContext();
        store.Append(enrichedEvent);

        foreach (var observer in observers)
        {
            observer.OnResourceEvent(enrichedEvent);
        }
    }
}
