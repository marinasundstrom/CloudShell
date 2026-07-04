namespace CloudShell.ControlPlane.Providers;

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
        public static readonly ResourceAttributeId Entrypoints = "loadBalancer.entrypointDefinitions";
        public static readonly ResourceAttributeId Routes = "loadBalancer.routeDefinitions";
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
                ValueType: ResourceAttributeValueType.String),
            [Attributes.HostResourceId] = new(
                DefaultValue: "default",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.EntrypointCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged),
            [Attributes.RouteCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged),
            [Attributes.HttpRouteCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged),
            [Attributes.TcpRouteCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged),
            [Attributes.EndpointCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged),
            [Attributes.Entrypoints] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: LoadBalancerShapeIds.Entrypoint),
            [Attributes.Routes] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: LoadBalancerShapeIds.Route)
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
                : new ResourceChangeApplyResult(changes, DeriveProviderManagedState(changes.ProposedState), diagnostics));
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

    private static ResourceState DeriveProviderManagedState(ResourceState state)
    {
        var entrypoints = state.ResourceAttributeValues
            .GetObject<LoadBalancerEntrypointValue[]>(
                Attributes.Entrypoints) ?? [];
        var routes = state.ResourceAttributeValues
            .GetObject<LoadBalancerRouteValue[]>(
                Attributes.Routes) ?? [];
        var attributes = state.ResourceAttributeValues.ToDictionary();
        attributes[Attributes.EntrypointCount] = ResourceAttributeValue.Integer(entrypoints.Length);
        attributes[Attributes.RouteCount] = ResourceAttributeValue.Integer(routes.Length);
        attributes[Attributes.HttpRouteCount] = ResourceAttributeValue.Integer(routes.Count(route =>
            string.Equals(route.Kind, "Http", StringComparison.OrdinalIgnoreCase)));
        attributes[Attributes.TcpRouteCount] = ResourceAttributeValue.Integer(routes.Count(route =>
            string.Equals(route.Kind, "Tcp", StringComparison.OrdinalIgnoreCase)));
        attributes[Attributes.EndpointCount] = ResourceAttributeValue.Integer(entrypoints.Length);

        return state with
        {
            Attributes = new ResourceAttributeValueMap(attributes)
        };
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
