using CloudShell.EventBroker.Client;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.EventBrokerService;

public sealed class EventBrokerServiceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly object gate = new();
    private readonly EventBrokerServiceOptions options;
    private readonly string eventsPath;
    private EventBrokerStoreDocument? document;

    public EventBrokerServiceStore(IOptions<EventBrokerServiceOptions> options)
    {
        this.options = options.Value;
        eventsPath = ResolveEventsPath(this.options);
    }

    public EventBrokerDefinition? GetBroker(string brokerId)
    {
        var definitions = LoadDefinitions();
        return definitions.FirstOrDefault(definition => string.Equals(
            definition.Id,
            brokerId,
            StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<EventBrokerStreamSummary> ListStreams(string brokerId)
    {
        EnsureBroker(brokerId);
        lock (gate)
        {
            var store = LoadStore();
            return store.Streams
                .OrderBy(stream => stream.Key, StringComparer.OrdinalIgnoreCase)
                .Select(stream =>
                {
                    var last = stream.Value.Events.LastOrDefault();
                    return new EventBrokerStreamSummary(
                        stream.Key,
                        stream.Value.Events.Count,
                        last?.Sequence ?? 0,
                        last?.Timestamp);
                })
                .ToArray();
        }
    }

    public EventBrokerEvent Append(
        string brokerId,
        string stream,
        EventBrokerPublishRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentNullException.ThrowIfNull(request);
        EnsureBroker(brokerId);

        lock (gate)
        {
            var store = LoadStore();
            if (!store.Streams.TryGetValue(stream, out var streamState))
            {
                streamState = new EventBrokerStreamState();
                store.Streams[stream] = streamState;
            }

            var nextSequence = streamState.Events.Count == 0
                ? 1
                : streamState.Events[^1].Sequence + 1;
            var retained = new EventBrokerEvent(
                nextSequence,
                Guid.NewGuid().ToString("n"),
                stream,
                request.Type.Trim(),
                DateTimeOffset.UtcNow,
                NormalizeOptional(request.Source),
                NormalizeOptional(request.Subject),
                request.Data.Clone(),
                NormalizeProperties(request.Properties));
            streamState.Events.Add(retained);
            SaveStore(store);
            return retained;
        }
    }

    public EventBrokerEventsResponse Read(
        string brokerId,
        string stream,
        long fromSequence,
        int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        EnsureBroker(brokerId);

        lock (gate)
        {
            var store = LoadStore();
            var normalizedLimit = Math.Clamp(limit, 1, 1000);
            var events = store.Streams.TryGetValue(stream, out var streamState)
                ? streamState.Events
                    .Where(item => item.Sequence > fromSequence)
                    .Take(normalizedLimit)
                    .ToArray()
                : [];

            return new EventBrokerEventsResponse(stream, fromSequence, events);
        }
    }

    private void EnsureBroker(string brokerId)
    {
        if (GetBroker(brokerId) is null)
        {
            throw new EventBrokerNotFoundException(brokerId);
        }
    }

    private EventBrokerStoreDocument LoadStore()
    {
        if (document is not null)
        {
            return document;
        }

        if (!File.Exists(eventsPath))
        {
            document = new EventBrokerStoreDocument();
            return document;
        }

        using var stream = File.OpenRead(eventsPath);
        document = JsonSerializer.Deserialize<EventBrokerStoreDocument>(
            stream,
            SerializerOptions) ?? new EventBrokerStoreDocument();
        return document;
    }

    private void SaveStore(EventBrokerStoreDocument store)
    {
        var directory = Path.GetDirectoryName(eventsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(eventsPath);
        JsonSerializer.Serialize(stream, store, SerializerOptions);
    }

    private IReadOnlyList<EventBrokerDefinition> LoadDefinitions()
    {
        if (string.IsNullOrWhiteSpace(options.DefinitionsPath) ||
            !File.Exists(options.DefinitionsPath))
        {
            return [];
        }

        using var stream = File.OpenRead(options.DefinitionsPath);
        var definitions = JsonSerializer.Deserialize<EventBrokerDefinition[]>(
            stream,
            SerializerOptions);
        return definitions ?? [];
    }

    private static string ResolveEventsPath(EventBrokerServiceOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.EventsPath))
        {
            return options.EventsPath;
        }

        if (!string.IsNullOrWhiteSpace(options.DefinitionsPath))
        {
            var directory = Path.GetDirectoryName(options.DefinitionsPath);
            var fileName = Path.GetFileNameWithoutExtension(options.DefinitionsPath);
            if (!string.IsNullOrWhiteSpace(directory) &&
                !string.IsNullOrWhiteSpace(fileName))
            {
                return Path.Combine(directory, $"{fileName}.events.json");
            }
        }

        return Path.Combine(Path.GetTempPath(), "CloudShell.EventBrokerService", "events.json");
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string> NormalizeProperties(
        IReadOnlyDictionary<string, string>? properties) =>
        properties?
            .Where(property => !string.IsNullOrWhiteSpace(property.Key))
            .ToDictionary(
                property => property.Key.Trim(),
                property => property.Value,
                StringComparer.OrdinalIgnoreCase) ??
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private sealed class EventBrokerStoreDocument
    {
        public Dictionary<string, EventBrokerStreamState> Streams { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class EventBrokerStreamState
    {
        public List<EventBrokerEvent> Events { get; set; } = [];
    }
}

public sealed class EventBrokerNotFoundException(string brokerId) : Exception(
    $"Event Broker '{brokerId}' was not found.")
{
    public string BrokerId { get; } = brokerId;
}
