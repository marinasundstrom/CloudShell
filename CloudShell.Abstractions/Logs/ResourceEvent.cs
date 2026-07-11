using System.Diagnostics;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Logs;

public sealed record ResourceEvent(
    string ResourceId,
    string EventType,
    string Message,
    DateTimeOffset Timestamp,
    string? TriggeredBy = null,
    ResourceSignalSeverity Severity = ResourceSignalSeverity.Info,
    string? TraceId = null,
    string? SpanId = null)
{
    public ResourceEvent WithCurrentTraceContext()
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return this;
        }

        return this with
        {
            TraceId = string.IsNullOrWhiteSpace(TraceId) ? activity.TraceId.ToString() : TraceId,
            SpanId = string.IsNullOrWhiteSpace(SpanId) ? activity.SpanId.ToString() : SpanId
        };
    }
}

public sealed record ResourceEventQuery(
    string? ResourceId = null,
    string? EventType = null,
    string? TriggeredBy = null,
    DateTimeOffset? Since = null,
    DateTimeOffset? Before = null,
    int MaxEvents = 200,
    string? TraceId = null,
    string? SpanId = null);

public interface IResourceEventSink
{
    void Append(ResourceEvent resourceEvent);
}

public interface IResourceEventObserver
{
    void OnResourceEvent(ResourceEvent resourceEvent);
}

public interface IResourceEventStore : IResourceEventSink
{
    IReadOnlyList<ResourceEvent> GetEvents(ResourceEventQuery? query = null);
}
