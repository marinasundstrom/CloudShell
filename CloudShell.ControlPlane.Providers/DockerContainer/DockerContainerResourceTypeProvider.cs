namespace CloudShell.ControlPlane.Providers;

public sealed class DockerContainerResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "container";
    public static readonly ResourceTypeId ResourceTypeId = "docker.container";
    public const string ProviderId = "docker";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId WorkloadKind = "workload.kind";
        public static readonly ResourceAttributeId ContainerImage = "container.image";
        public static readonly ResourceAttributeId ContainerRegistry = "container.registry";
        public static readonly ResourceAttributeId ContainerReplicas = "container.replicas";
        public static readonly ResourceAttributeId EndpointCount = "endpoints.count";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Stop = "stop";
        public static readonly ResourceOperationId Pause = "pause";
        public static readonly ResourceOperationId Restart = "restart";
        public static readonly ResourceOperationId Unpause = "docker.unpause";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.WorkloadKind] = new(
                DefaultValue: "ContainerImage",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ContainerImage] = new(
                Required: true,
                RequiredMessage: "Container image is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ContainerRegistry] = new(
                DefaultValue: "docker.io",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ContainerReplicas] = new(
                DefaultValue: 1,
                ValueType: ResourceAttributeValueType.Integer),
            [Attributes.EndpointCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged)
        },
        Capabilities:
        [
            new(ResourceCommonCapabilityIds.Monitoring)
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Stop),
            new(Operations.Pause),
            new(Operations.Restart),
            new(Operations.Unpause)
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
                    "Accept Docker container definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize Docker container resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateContainerImage(resource.Attributes.GetString(Attributes.ContainerImage), diagnostics);
        ValidatePositiveInteger(
            resource.Attributes.GetString(Attributes.ContainerReplicas),
            Attributes.ContainerReplicas,
            "docker.container.replicasInvalid",
            "Container replicas must be a positive integer.",
            diagnostics);
        ValidateNonNegativeInteger(
            resource.Attributes.GetString(Attributes.EndpointCount),
            Attributes.EndpointCount,
            "docker.container.endpointCountInvalid",
            "Endpoint count must be a non-negative integer.",
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
            ValidatePositiveInteger(
                replicas,
                Attributes.ContainerReplicas,
                "docker.container.replicasInvalid",
                "Container replicas must be a positive integer.",
                diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.EndpointCount, out var endpointCount))
        {
            ValidateNonNegativeInteger(
                endpointCount,
                Attributes.EndpointCount,
                "docker.container.endpointCountInvalid",
                "Endpoint count must be a non-negative integer.",
                diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateContainerImage(
        string? image,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "docker.container.imageRequired",
                "Container image is required.",
                Attributes.ContainerImage));
        }
    }

    private static void ValidatePositiveInteger(
        string? value,
        ResourceAttributeId attributeId,
        string code,
        string message,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            (!int.TryParse(value, out var integer) || integer < 1))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                code,
                message,
                attributeId));
        }
    }

    private static void ValidateNonNegativeInteger(
        string? value,
        ResourceAttributeId attributeId,
        string code,
        string message,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            (!int.TryParse(value, out var integer) || integer < 0))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                code,
                message,
                attributeId));
        }
    }
}
