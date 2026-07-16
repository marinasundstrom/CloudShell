using ResourceOrchestratorSessionAffinityMode = CloudShell.Abstractions.ResourceManager.ResourceOrchestratorSessionAffinityMode;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "container";
    public static readonly ResourceTypeId ResourceTypeId = "application.container-app";
    public const string ProviderId = "applications.container-app";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId ContainerImage = "container.image";
        public static readonly ResourceAttributeId ContainerRegistry = "container.registry";
        public static readonly ResourceAttributeId ContainerBuildContext = "container.buildContext";
        public static readonly ResourceAttributeId ContainerDockerfile = "container.dockerfile";
        public static readonly ResourceAttributeId ContainerReplicas = "container.replicas";
        public static readonly ResourceAttributeId EndpointRequests = "container.endpointRequests";
        public static readonly ResourceAttributeId RoutingSessionAffinityMode = "container.routing.sessionAffinity.mode";
        public static readonly ResourceAttributeId RoutingSessionAffinityCookieName = "container.routing.sessionAffinity.cookieName";
        public static readonly ResourceAttributeId RoutingSessionAffinityDurationSeconds = "container.routing.sessionAffinity.durationSeconds";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Stop = "stop";
        public static readonly ResourceOperationId Restart = "restart";
        public static readonly ResourceOperationId UpdateImage = "container.image.update";
        public static readonly ResourceOperationId UpdateReplicas = "container.replicas.update";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.ContainerImage] = new(
                Required: true,
                RequiredMessage: "Container image is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ContainerRegistry] = new(
                DefaultValue: "docker.io",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ContainerBuildContext] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ContainerDockerfile] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ContainerReplicas] = new(
                DefaultValue: 1,
                ValueType: ResourceAttributeValueType.Integer),
            [Attributes.EndpointRequests] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: NetworkingEndpointShapeIds.EndpointRequest,
                path: "endpoints"),
            [Attributes.RoutingSessionAffinityMode] = new(
                DefaultValue: "None",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.RoutingSessionAffinityCookieName] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.RoutingSessionAffinityDurationSeconds] = new(
                ValueType: ResourceAttributeValueType.Integer)
        },
        Capabilities:
        [
            new(ResourceCommonCapabilityIds.EndpointSource),
            new(ResourceCommonCapabilityIds.Monitoring),
            new(VolumeConsumerCapabilityProvider.CapabilityIdValue)
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Stop),
            new(Operations.Restart),
            new(Operations.UpdateImage),
            new(Operations.UpdateReplicas)
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
                    "Accept container application definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize container application resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateContainerImage(
            resource.Attributes.GetString(Attributes.ContainerImage),
            diagnostics);
        ValidateContainerReplicas(
            resource.Attributes.GetString(Attributes.ContainerReplicas),
            diagnostics);
        ValidateRoutingSessionAffinity(
            resource.Attributes.GetString(Attributes.RoutingSessionAffinityMode),
            resource.Attributes.GetString(Attributes.RoutingSessionAffinityCookieName),
            resource.Attributes.GetString(Attributes.RoutingSessionAffinityDurationSeconds),
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.ContainerImage, out var image))
        {
            ValidateContainerImage(image, diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.ContainerReplicas, out var replicas))
        {
            ValidateContainerReplicas(replicas, diagnostics);
        }

        state.ResourceAttributes.TryGetValue(Attributes.RoutingSessionAffinityMode, out var sessionAffinityMode);
        state.ResourceAttributes.TryGetValue(Attributes.RoutingSessionAffinityCookieName, out var sessionAffinityCookieName);
        state.ResourceAttributes.TryGetValue(Attributes.RoutingSessionAffinityDurationSeconds, out var sessionAffinityDurationSeconds);
        ValidateRoutingSessionAffinity(
            sessionAffinityMode,
            sessionAffinityCookieName,
            sessionAffinityDurationSeconds,
            diagnostics);

        return diagnostics;
    }

    private static void ValidateContainerImage(
        string? image,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.container.imageRequired",
                "Container image is required.",
                Attributes.ContainerImage));
        }
    }

    private static void ValidateContainerReplicas(
        string? replicas,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(replicas) &&
            (!int.TryParse(replicas, out var replicaCount) || replicaCount < 1))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.container.replicasInvalid",
                "Container replicas must be a positive integer.",
                Attributes.ContainerReplicas));
        }
    }

    private static void ValidateRoutingSessionAffinity(
        string? mode,
        string? cookieName,
        string? durationSeconds,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(mode) &&
            !Enum.TryParse<ResourceOrchestratorSessionAffinityMode>(
                mode,
                ignoreCase: true,
                out var parsedMode))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.container.routingSessionAffinityModeInvalid",
                "Container app session affinity mode must be None, ClientIp, or Cookie.",
                Attributes.RoutingSessionAffinityMode));
            return;
        }

        var effectiveMode = string.IsNullOrWhiteSpace(mode)
            ? ResourceOrchestratorSessionAffinityMode.None
            : Enum.Parse<ResourceOrchestratorSessionAffinityMode>(
                mode,
                ignoreCase: true);
        if (effectiveMode == ResourceOrchestratorSessionAffinityMode.Cookie &&
            string.IsNullOrWhiteSpace(cookieName))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.container.routingSessionAffinityCookieNameRequired",
                "Container app cookie session affinity requires a cookie name.",
                Attributes.RoutingSessionAffinityCookieName));
        }

        if (!string.IsNullOrWhiteSpace(durationSeconds) &&
            (!int.TryParse(durationSeconds, out var seconds) || seconds < 1))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.container.routingSessionAffinityDurationInvalid",
                "Container app session affinity duration must be a positive integer.",
                Attributes.RoutingSessionAffinityDurationSeconds));
        }
    }

    internal static bool TryGetContainerHostResourceId(
        ResourceState state,
        out string containerHostResourceId)
    {
        foreach (var reference in state.StartupDependencies)
        {
            if (reference.TypeId is { } typeId &&
                IsContainerHostResourceType(typeId) &&
                reference.TryGetDependsOnResourceId(out containerHostResourceId))
            {
                return true;
            }
        }

        containerHostResourceId = string.Empty;
        return false;
    }

    internal static bool IsContainerHostResourceType(ResourceTypeId typeId) =>
        typeId == ContainerHostResourceTypeProvider.ResourceTypeId ||
        typeId == DockerHostResourceTypeProvider.ResourceTypeId;
}
