namespace CloudShell.Abstractions.Logs;

public sealed record ResourceEvent(
    string ResourceId,
    string EventType,
    string Message,
    DateTimeOffset Timestamp,
    string? TriggeredBy = null,
    string Level = "Information");

public sealed record ResourceEventQuery(
    string? ResourceId = null,
    string? EventType = null,
    string? TriggeredBy = null,
    DateTimeOffset? Since = null,
    DateTimeOffset? Before = null,
    int MaxEvents = 200);

public interface IResourceEventSink
{
    void Append(ResourceEvent resourceEvent);
}

public interface IResourceEventStore : IResourceEventSink
{
    IReadOnlyList<ResourceEvent> GetEvents(ResourceEventQuery? query = null);
}
