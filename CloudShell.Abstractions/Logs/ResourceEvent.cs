namespace CloudShell.Abstractions.Logs;

public sealed record ResourceEvent(
    string ResourceId,
    string EventType,
    string Message,
    DateTimeOffset Timestamp,
    string? TriggeredBy = null,
    string Level = "Information");

public interface IResourceEventSink
{
    void Append(ResourceEvent resourceEvent);
}
