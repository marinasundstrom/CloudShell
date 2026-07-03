namespace CloudShell.ControlPlane.Providers;

public sealed class ExecutableApplicationResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "executable";
    public static readonly ResourceTypeId ResourceTypeId = "application.executable";
    public const string ProviderId = "applications.executable";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId Command = "command";
        public static readonly ResourceAttributeId ExecutablePath = "path";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.Command] = new(
                ValueType: ResourceAttributeValueType.ComplexType),
            [Attributes.ExecutablePath] = new(
                Required: true,
                RequiredMessage: "Executable path is required.",
                ValueType: ResourceAttributeValueType.String)
        },
        Capabilities:
        [
            new(ResourceCommonCapabilityIds.Monitoring),
            new(VolumeConsumerCapabilityProvider.CapabilityIdValue),
            new(
                ResourceLogSourceCapabilityIds.LogSources,
                ResourceDefinitionJson.FromValue(ResourceLogSourceDefinitionSet.DefaultConsole()))
        ],
        Operations:
        [
            new(Operations.Start)
        ]);

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var configuration = resource.Attributes.GetObject<ExecutableApplicationConfiguration>(
            Attributes.Command);
        var path = FirstNonEmpty(
            resource.Attributes.GetString(Attributes.ExecutablePath),
            configuration?.Path);

        if (string.IsNullOrWhiteSpace(path))
        {
            return ValueTask.FromResult(ResourceDefinitionValidationResult.FromDiagnostics(
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.executable.pathRequired",
                        "Executable path is required.",
                        Attributes.ExecutablePath)
                ]));
        }

        return ValueTask.FromResult(ResourceDefinitionValidationResult.Success);
    }

    public bool CanPlan(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionApplyPlan> PlanApplyAsync(
        Resource resource,
        ResourceDefinitionApplyContext context,
        CancellationToken cancellationToken = default)
    {
        var configuration = resource.Attributes.GetObject<ExecutableApplicationConfiguration>(
            Attributes.Command);
        var path = FirstNonEmpty(
            resource.Attributes.GetString(Attributes.ExecutablePath),
            configuration?.Path);
        var resourceId = resource.EffectiveResourceId;

        return ValueTask.FromResult(new ResourceDefinitionApplyPlan(
            resource,
            [
                new(
                    resourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.AcceptDefinition,
                    "Accept executable application definition.",
                    resource.ToDefinition()),
                new(
                    resourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize executable application runtime state for '{path}'.")
            ],
            []));
    }

    public bool CanApply(ResourceChangeSet changes) =>
        changes.Resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>(changes.Diagnostics);

        if (!changes.ProposedState.ResourceAttributes.TryGetValue(Attributes.ExecutablePath, out var path) ||
            string.IsNullOrWhiteSpace(path))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.executable.pathRequired",
                "Executable path is required.",
                Attributes.ExecutablePath));
        }

        var result = diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
                ? ResourceChangeApplyResult.Rejected(changes, diagnostics)
                : new ResourceChangeApplyResult(changes, changes.ProposedState, diagnostics);

        return ValueTask.FromResult(result);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
