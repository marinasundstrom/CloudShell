namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ExecutableApplicationResourceTypeProvider :
    IResourceTypeProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "executable";
    public static readonly ResourceTypeId ResourceTypeId = "application.executable";
    public const string ProviderId = "applications.executable";
    public const string ConfigurationSection = "executable";

    public static class Attributes
    {
        public static readonly ResourceAttributeId ExecutablePath = "executable.path";
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
        RequiredAttributes:
        [
            new(Attributes.ExecutablePath, "Executable path is required.")
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
        var configuration = resource.GetConfiguration<ExecutableApplicationConfiguration>(
            ConfigurationSection);

        if (string.IsNullOrWhiteSpace(configuration?.Path))
        {
            return ValueTask.FromResult(ResourceDefinitionValidationResult.FromDiagnostics(
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.executable.pathRequired",
                        "Executable configuration path is required.",
                        ConfigurationSection)
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
        var configuration = resource.GetConfiguration<ExecutableApplicationConfiguration>(
            ConfigurationSection);
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
                    $"Materialize executable application runtime state for '{configuration?.Path}'.")
            ],
            []));
    }
}

public sealed record ExecutableApplicationConfiguration(
    string Path,
    string? Arguments,
    string? WorkingDirectory = null);
