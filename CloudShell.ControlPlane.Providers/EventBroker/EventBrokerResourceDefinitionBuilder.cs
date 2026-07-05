namespace CloudShell.ControlPlane.Providers;

public sealed class EventBrokerResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<EventBrokerResourceDefinitionBuilder>(name)
{
    private readonly List<EventBrokerProtocolEndpoint> _protocols = [];

    protected override ResourceTypeId TypeId =>
        EventBrokerResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        EventBrokerResourceTypeProvider.ProviderId;

    public EventBrokerResourceDefinitionBuilder AddProtocol(
        string name,
        string protocol,
        string endpoint,
        string? eventFormat = null,
        IReadOnlyList<string>? capabilities = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        _protocols.RemoveAll(current => string.Equals(
            current.Name,
            name,
            StringComparison.OrdinalIgnoreCase));
        _protocols.Add(new(
            name.Trim(),
            protocol.Trim().ToLowerInvariant(),
            endpoint.Trim(),
            string.IsNullOrWhiteSpace(eventFormat) ? null : eventFormat.Trim(),
            NormalizeCapabilities(capabilities)));

        SetObjectAttribute(
            EventBrokerResourceTypeProvider.Attributes.Protocols,
            _protocols.ToArray());

        if (string.Equals(protocol, EventBrokerProtocols.Http, StringComparison.OrdinalIgnoreCase))
        {
            SetScalarAttribute(EventBrokerResourceTypeProvider.Attributes.Endpoint, endpoint.Trim());
        }

        return this;
    }

    public EventBrokerResourceDefinitionBuilder WithMqttEndpoint(
        string endpoint,
        string name = "mqtt",
        string? eventFormat = "json",
        IReadOnlyList<string>? capabilities = null) =>
        AddProtocol(name, EventBrokerProtocols.Mqtt, endpoint, eventFormat, capabilities);

    public EventBrokerResourceDefinitionBuilder WithHttpEndpoint(
        string endpoint,
        string name = "http",
        string? eventFormat = "json",
        IReadOnlyList<string>? capabilities = null) =>
        AddProtocol(name, EventBrokerProtocols.Http, endpoint, eventFormat, capabilities);

    public EventBrokerResourceDefinitionBuilder WithAmqpEndpoint(
        string endpoint,
        string name = "amqp",
        string? eventFormat = "json",
        IReadOnlyList<string>? capabilities = null) =>
        AddProtocol(name, EventBrokerProtocols.Amqp, endpoint, eventFormat, capabilities);

    public EventBrokerResourceDefinitionBuilder WithKafkaEndpoint(
        string endpoint,
        string name = "kafka",
        string? eventFormat = "json",
        IReadOnlyList<string>? capabilities = null) =>
        AddProtocol(name, EventBrokerProtocols.Kafka, endpoint, eventFormat, capabilities);

    public EventBrokerResourceDefinitionBuilder WithEventHubsEndpoint(
        string endpoint,
        string name = "eventhubs",
        string? eventFormat = "json",
        IReadOnlyList<string>? capabilities = null) =>
        AddProtocol(name, EventBrokerProtocols.EventHubs, endpoint, eventFormat, capabilities);

    public EventBrokerResourceDefinitionBuilder WithNatsEndpoint(
        string endpoint,
        string name = "nats",
        string? eventFormat = "json",
        IReadOnlyList<string>? capabilities = null) =>
        AddProtocol(name, EventBrokerProtocols.Nats, endpoint, eventFormat, capabilities);

    private static IReadOnlyList<string>? NormalizeCapabilities(
        IReadOnlyList<string>? capabilities)
    {
        var normalized = capabilities?
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Select(capability => capability.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized is { Length: > 0 }
            ? normalized
            : null;
    }
}

public static class EventBrokerProtocols
{
    public const string Mqtt = "mqtt";
    public const string Http = "http";
    public const string Amqp = "amqp";
    public const string Kafka = "kafka";
    public const string EventHubs = "eventhubs";
    public const string Nats = "nats";
}

public static class EventBrokerProtocolCapabilities
{
    public const string PublishEvents = "events.publish";
    public const string SubscribeEvents = "events.subscribe";
    public const string RetainedEvents = "events.retained";
    public const string TelemetryIngestion = "telemetry.ingest";
}

public static class EventBrokerResourceDefinitionBuilderExtensions
{
    public static EventBrokerResourceDefinitionBuilder AddEventBroker(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new EventBrokerResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
