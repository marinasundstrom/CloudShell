using System.Text.Json;

namespace CloudShell.EventBroker.Client;

public sealed record EventBrokerPublishRequest(
    string Type,
    JsonElement Data,
    string? Source = null,
    string? Subject = null,
    IReadOnlyDictionary<string, string>? Properties = null);

public sealed record EventBrokerEvent(
    long Sequence,
    string Id,
    string Stream,
    string Type,
    DateTimeOffset Timestamp,
    string? Source,
    string? Subject,
    JsonElement Data,
    IReadOnlyDictionary<string, string> Properties);

public sealed record EventBrokerEventsResponse(
    string Stream,
    long FromSequence,
    IReadOnlyList<EventBrokerEvent> Events);

public sealed record EventBrokerStreamSummary(
    string Name,
    long EventCount,
    long LastSequence,
    DateTimeOffset? LastEventAt);
