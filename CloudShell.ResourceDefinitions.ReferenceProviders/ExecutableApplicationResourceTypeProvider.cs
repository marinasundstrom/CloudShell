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

public sealed class ExecutableStartOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    public ResourceOperationId OperationId =>
        ExecutableApplicationResourceTypeProvider.Operations.Start;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId &&
        operation.IsAvailable;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ResourceDefinitionValidationResult.Success);

    public bool CanProject(
        Resource resource,
        ResourceOperationResolution operation) =>
        CanHandle(resource, operation);

    public ValueTask<IResourceOperationProjection> ProjectAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceOperationProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceOperationProjection>(
            new ExecutableStartOperation(resource, operation));
}

public sealed class ExecutableStartOperation(
    Resource resource,
    ResourceOperationResolution operation) : IResourceOperationProjection
{
    public Resource Resource { get; } = resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => ExecutableApplicationResourceTypeProvider.Operations.Start;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public ResourceDefinitionApplyStep PlanStart() =>
        new(
            Resource.EffectiveResourceId,
            Resource.Type.TypeId,
            ResourceDefinitionApplyStepKind.MaterializeRuntime,
            $"Start executable application resource '{Resource.Name}'.");

    public async ValueTask<ResourceOperationExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await CanExecuteAsync(cancellationToken))
        {
            return new ResourceOperationExecutionResult(
                Resource,
                OperationId,
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.executable.startUnavailable",
                        UnavailableReason ?? "The start operation is not available.",
                        OperationId)
                ]);
        }

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            []);
    }
}
