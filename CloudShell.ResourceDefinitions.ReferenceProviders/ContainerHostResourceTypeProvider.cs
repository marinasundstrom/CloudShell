namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ContainerHostResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "infrastructure";
    public static readonly ResourceTypeId ResourceTypeId = "cloudshell.container-host";
    public const string ProviderId = "container-host.reference";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId InfrastructureKind = "infrastructure.kind";
        public static readonly ResourceAttributeId HostKind = "container.host.kind";
        public static readonly ResourceAttributeId Endpoint = "container.host.endpoint";
        public static readonly ResourceAttributeId Registry = "container.registry";
        public static readonly ResourceAttributeId IsDefault = "container.host.default";
    }

    public static class Capabilities
    {
        public static readonly ResourceCapabilityId ContainerImage = "container.image";
        public static readonly ResourceCapabilityId ContainerBuild = "container.build";
        public static readonly ResourceCapabilityId StorageMountFileSystem = "storage.mount.filesystem";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Inspect = "container.host.inspect";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.InfrastructureKind] = new(
                DefaultValue: "containerHost",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.HostKind] = new(
                DefaultValue: "Docker",
                Required: true,
                RequiredMessage: "Container host kind is required.",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.Endpoint] = new(
                DefaultValue: "unix:///var/run/docker.sock",
                Required: true,
                RequiredMessage: "Container host endpoint is required.",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.Registry] = new(
                DefaultValue: "docker.io",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.IsDefault] = new(
                DefaultValue: true,
                ValueShape: new(ResourceAttributeValueKind.Boolean))
        },
        Capabilities:
        [
            new(Capabilities.ContainerImage),
            new(Capabilities.ContainerBuild),
            new(Capabilities.StorageMountFileSystem)
        ],
        Operations:
        [
            new(Operations.Inspect)
        ]);

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = ValidateResolvedResource(resource);

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

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
                    "Accept container host definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize container host resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateRequired(
            resource.Attributes.GetString(Attributes.HostKind),
            Attributes.HostKind,
            "application.containerHost.kindRequired",
            "Container host kind is required.",
            diagnostics);
        ValidateRequired(
            resource.Attributes.GetString(Attributes.Endpoint),
            Attributes.Endpoint,
            "application.containerHost.endpointRequired",
            "Container host endpoint is required.",
            diagnostics);
        ValidateBoolean(
            resource.Attributes.GetString(Attributes.IsDefault),
            Attributes.IsDefault,
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.HostKind, out var hostKind))
        {
            ValidateRequired(
                hostKind,
                Attributes.HostKind,
                "application.containerHost.kindRequired",
                "Container host kind is required.",
                diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.Endpoint, out var endpoint))
        {
            ValidateRequired(
                endpoint,
                Attributes.Endpoint,
                "application.containerHost.endpointRequired",
                "Container host endpoint is required.",
                diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.IsDefault, out var isDefault))
        {
            ValidateBoolean(isDefault, Attributes.IsDefault, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateRequired(
        string? value,
        ResourceAttributeId attributeId,
        string code,
        string message,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                code,
                message,
                attributeId));
        }
    }

    private static void ValidateBoolean(
        string? value,
        ResourceAttributeId attributeId,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !bool.TryParse(value, out _))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.containerHost.booleanInvalid",
                "Container host boolean attributes must be valid booleans.",
                attributeId));
        }
    }
}
