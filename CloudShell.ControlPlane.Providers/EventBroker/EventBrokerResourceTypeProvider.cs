namespace CloudShell.ControlPlane.Providers;

public sealed class EventBrokerResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "service";
    public static readonly ResourceTypeId ResourceTypeId = "event.broker";
    public const string ProviderId = "event.broker";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId Endpoint = "endpoint";
        public static readonly ResourceAttributeId Kind = "kind";
        public static readonly ResourceAttributeId Protocols = "protocols";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.Endpoint] = new(
                Description: "HTTP Event Broker service endpoint.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Kind] = new(
                DefaultValue: "broker",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Protocols] = ResourceAttributeDefinition.Collection(
                ResourceAttributeValueType.ComplexType,
                itemShape: new(new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    ["name"] = new(
                        Required: true,
                        RequiredMessage: "Event Broker protocol name is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["protocol"] = new(
                        Required: true,
                        RequiredMessage: "Event Broker protocol is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["endpoint"] = new(
                        Required: true,
                        RequiredMessage: "Event Broker protocol endpoint is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["eventFormat"] = new(ValueType: ResourceAttributeValueType.String),
                    ["capabilities"] = ResourceAttributeDefinition.Collection(
                        ResourceAttributeValueType.String)
                }),
                description: "Protocol endpoints exposed by this broker, such as MQTT, HTTP, AMQP, Kafka, Event Hubs, or NATS.")
        },
        Capabilities:
        [
            new(ResourceCommonCapabilityIds.EndpointSource)
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Stop),
            new(Operations.Restart)
        ]);

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Stop = "stop";
        public static readonly ResourceOperationId Restart = "restart";
    }

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateProtocolEndpointCount(
            resource.Attributes.GetObject<EventBrokerProtocolEndpoint[]>(Attributes.Protocols) ?? [],
            diagnostics);

        return ValueTask.FromResult(ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

    public bool CanApply(ResourceChangeSet changes) =>
        changes.Resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>(changes.Diagnostics);
        ValidateProtocolEndpointCount(
            changes.ProposedState.ResourceAttributeValues.GetObject<EventBrokerProtocolEndpoint[]>(
                Attributes.Protocols) ?? [],
            diagnostics);

        return ValueTask.FromResult(diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
                ? ResourceChangeApplyResult.Rejected(changes, diagnostics)
                : new ResourceChangeApplyResult(changes, changes.ProposedState, diagnostics));
    }

    public bool CanPlan(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionApplyPlan> PlanApplyAsync(
        Resource resource,
        ResourceDefinitionApplyContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ResourceDefinitionApplyPlan(
            resource,
            [
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.AcceptDefinition,
                    "Accept Event Broker definition.",
                    resource.ToDefinition())
            ],
            []));

    private static void ValidateProtocolEndpointCount(
        IReadOnlyList<EventBrokerProtocolEndpoint> protocols,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var protocol in protocols)
        {
            if (string.IsNullOrWhiteSpace(protocol.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "event.broker.protocolNameRequired",
                    "Event Broker protocol name is required.",
                    Attributes.Protocols));
            }
            else if (!names.Add(protocol.Name.Trim()))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "event.broker.protocolNameDuplicate",
                    $"Event Broker protocol '{protocol.Name}' is declared more than once.",
                    Attributes.Protocols));
            }

            if (string.IsNullOrWhiteSpace(protocol.Protocol))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "event.broker.protocolRequired",
                    "Event Broker protocol is required.",
                    Attributes.Protocols));
            }

            if (string.IsNullOrWhiteSpace(protocol.Endpoint))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "event.broker.protocolEndpointRequired",
                    "Event Broker protocol endpoint is required.",
                    Attributes.Protocols));
            }
        }
    }
}

public sealed record EventBrokerProtocolEndpoint(
    string Name,
    string Protocol,
    string Endpoint,
    string? EventFormat = null,
    IReadOnlyList<string>? Capabilities = null);

public sealed record EventBrokerStreamDefinition(
    string Name,
    string? Description = null,
    IReadOnlyList<string>? EventTypes = null);
