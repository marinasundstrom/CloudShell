namespace CloudShell.ControlPlane.Providers;

public enum ContainerApplicationRuntimeStatus
{
    Unknown,
    Stopped,
    Running
}

public interface IContainerApplicationRuntimeHandler
{
    ContainerApplicationRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        Resource resource,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopContainerApplicationRuntimeHandler :
    IContainerApplicationRuntimeHandler
{
    public const string RuntimeUnavailableDiagnosticCode =
        "application.container.runtimeUnavailable";

    public ContainerApplicationRuntimeStatus GetStatus(Resource resource) =>
        ContainerApplicationRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        RuntimeUnavailableAsync(resource, operationId);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        RuntimeUnavailableAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.UpdateImage);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        RuntimeUnavailableAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.UpdateReplicas);

    private static ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> RuntimeUnavailableAsync(
        Resource resource,
        ResourceOperationId operationId) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            ResourceDefinitionDiagnostic.Error(
                RuntimeUnavailableDiagnosticCode,
                $"No container application runtime is configured for this host. The '{operationId}' operation cannot be applied until a container app runtime or orchestrator is registered.",
                resource.EffectiveResourceId)
        ]);
}
