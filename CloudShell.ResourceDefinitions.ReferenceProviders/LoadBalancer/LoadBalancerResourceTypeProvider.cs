namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class LoadBalancerResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "network";
    public static readonly ResourceTypeId ResourceTypeId = "cloudshell.loadBalancer";
    public const string ProviderId = "cloudshell.load-balancer";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId Provider = "loadBalancer.provider";
        public static readonly ResourceAttributeId HostResourceId = "loadBalancer.hostResourceId";
        public static readonly ResourceAttributeId EntrypointCount = "loadBalancer.entrypoints";
        public static readonly ResourceAttributeId RouteCount = "loadBalancer.routes";
        public static readonly ResourceAttributeId HttpRouteCount = "loadBalancer.routes.http";
        public static readonly ResourceAttributeId TcpRouteCount = "loadBalancer.routes.tcp";
        public static readonly ResourceAttributeId EndpointCount = "endpoints.count";
    }

    public static class Capabilities
    {
        public static readonly ResourceCapabilityId NetworkingProvider = "networking.provider";
        public static readonly ResourceCapabilityId NetworkingEndpointProvider = "networking.endpointProvider";
        public static readonly ResourceCapabilityId NetworkingEndpointMapper = "networking.endpointMapper";
        public static readonly ResourceCapabilityId NetworkingGateway = "networking.gateway";
        public static readonly ResourceCapabilityId NetworkingLoadBalancer = "networking.loadBalancer";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId ApplyConfiguration = "applyLoadBalancerConfiguration";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.Provider] = new(
                DefaultValue: "logical",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.HostResourceId] = new(
                DefaultValue: "default",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.EntrypointCount] = new(
                DefaultValue: 0,
                ValueShape: new(ResourceAttributeValueKind.Integer)),
            [Attributes.RouteCount] = new(
                DefaultValue: 0,
                ValueShape: new(ResourceAttributeValueKind.Integer)),
            [Attributes.HttpRouteCount] = new(
                DefaultValue: 0,
                ValueShape: new(ResourceAttributeValueKind.Integer)),
            [Attributes.TcpRouteCount] = new(
                DefaultValue: 0,
                ValueShape: new(ResourceAttributeValueKind.Integer)),
            [Attributes.EndpointCount] = new(
                DefaultValue: 0,
                ValueShape: new(ResourceAttributeValueKind.Integer))
        },
        Capabilities:
        [
            new(Capabilities.NetworkingProvider),
            new(Capabilities.NetworkingEndpointProvider),
            new(Capabilities.NetworkingEndpointMapper),
            new(Capabilities.NetworkingGateway),
            new(Capabilities.NetworkingLoadBalancer)
        ],
        Operations:
        [
            new(Operations.ApplyConfiguration)
        ]);

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ResourceDefinitionValidationResult.FromDiagnostics(
            ValidateResolvedResource(resource)));

    public bool CanApply(ResourceChangeSet changes) =>
        changes.Resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>(changes.Diagnostics);
        diagnostics.AddRange(ValidateExplicitState(changes.ProposedState));

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
                    "Accept load balancer definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize load balancer resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateNonNegativeInteger(
            resource.Attributes.GetString(Attributes.EntrypointCount),
            Attributes.EntrypointCount,
            diagnostics);
        ValidateNonNegativeInteger(
            resource.Attributes.GetString(Attributes.RouteCount),
            Attributes.RouteCount,
            diagnostics);
        ValidateNonNegativeInteger(
            resource.Attributes.GetString(Attributes.HttpRouteCount),
            Attributes.HttpRouteCount,
            diagnostics);
        ValidateNonNegativeInteger(
            resource.Attributes.GetString(Attributes.TcpRouteCount),
            Attributes.TcpRouteCount,
            diagnostics);
        ValidateNonNegativeInteger(
            resource.Attributes.GetString(Attributes.EndpointCount),
            Attributes.EndpointCount,
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        foreach (var attributeId in new[]
        {
            Attributes.EntrypointCount,
            Attributes.RouteCount,
            Attributes.HttpRouteCount,
            Attributes.TcpRouteCount,
            Attributes.EndpointCount
        })
        {
            if (state.ResourceAttributes.TryGetValue(attributeId, out var value))
            {
                ValidateNonNegativeInteger(value, attributeId, diagnostics);
            }
        }

        return diagnostics;
    }

    private static void ValidateNonNegativeInteger(
        string? value,
        ResourceAttributeId attributeId,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            (!int.TryParse(value, out var count) || count < 0))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "network.loadBalancer.countInvalid",
                "Load balancer count attributes must be non-negative integers.",
                attributeId));
        }
    }
}
