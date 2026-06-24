namespace CloudShell.ResourceDefinitions.ReferenceProviders;

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
        public static readonly ResourceAttributeId ContainerReplicas = "container.replicas";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Restart = "restart";
        public static readonly ResourceOperationId UpdateImage = "container.image.update";
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
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.ContainerReplicas] = new(
                DefaultValue: 1,
                ValueShape: new(ResourceAttributeValueKind.Integer))
        },
        Operations:
        [
            new(Operations.Start),
            new(Operations.Restart),
            new(Operations.UpdateImage)
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
}

public sealed class ContainerApplicationStartOperationProvider :
    ContainerApplicationLifecycleOperationProvider
{
    public ContainerApplicationStartOperationProvider()
        : base(ContainerApplicationResourceTypeProvider.Operations.Start)
    {
    }
}

public sealed class ContainerApplicationRestartOperationProvider :
    ContainerApplicationLifecycleOperationProvider
{
    public ContainerApplicationRestartOperationProvider()
        : base(ContainerApplicationResourceTypeProvider.Operations.Restart)
    {
    }
}

public sealed class ContainerApplicationImageUpdateOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    public ResourceOperationId OperationId =>
        ContainerApplicationResourceTypeProvider.Operations.UpdateImage;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId &&
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
            new ContainerApplicationImageUpdateOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation));
}

public sealed class ContainerApplicationImageUpdateOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => ContainerApplicationResourceTypeProvider.Operations.UpdateImage;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public ResourceChangeSet UpdateImage(
        string image,
        int? replicas = null)
    {
        using var changes = Context.CreateChangeContext();
        changes.SetAttribute(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage,
            image);

        if (replicas is not null)
        {
            changes.SetAttribute(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas,
                replicas.Value);
        }

        return changes.ApplyChanges();
    }

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
                        "application.container.imageUpdateUnavailable",
                        UnavailableReason ?? "The container image update operation is not available.",
                        OperationId)
                ]);
        }

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            []);
    }
}

public abstract class ContainerApplicationLifecycleOperationProvider(
    ResourceOperationId operationId) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    public ResourceOperationId OperationId { get; } = operationId;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId &&
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
            new ContainerApplicationLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation));
}

public sealed class ContainerApplicationLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

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
                        "application.container.operationUnavailable",
                        UnavailableReason ?? $"The '{OperationId}' operation is not available.",
                        OperationId)
                ]);
        }

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            []);
    }
}
