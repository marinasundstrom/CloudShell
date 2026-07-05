using CloudShell.ControlPlane.Providers;

namespace CloudShell.EventBrokerService;

public sealed record EventBrokerDefinition(
    string Id,
    string Name,
    string? DisplayName,
    string? Endpoint,
    IReadOnlyList<EventBrokerProtocolEndpoint>? Protocols);
