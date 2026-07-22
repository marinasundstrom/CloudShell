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

public interface IContainerApplicationRuntimeReadinessProvider
{
    string? GetOperationUnavailableReason(
        Resource resource,
        ResourceOperationId operationId);
}

public static class ContainerApplicationRuntimeReadiness
{
    public static string? GetOperationUnavailableReason(
        IContainerApplicationRuntimeHandler? runtimeHandler,
        Resource resource,
        ResourceOperationId operationId)
    {
        if (NoopContainerApplicationRuntimeHandler.IsMissing(runtimeHandler))
        {
            return NoopContainerApplicationRuntimeHandler.CreateRuntimeUnavailableReason(
                resource,
                operationId);
        }

        return runtimeHandler is IContainerApplicationRuntimeReadinessProvider readinessProvider
            ? readinessProvider.GetOperationUnavailableReason(resource, operationId)
            : null;
    }
}

public sealed class NoopContainerApplicationRuntimeHandler :
    IContainerApplicationRuntimeHandler
{
    public const string RuntimeUnavailableDiagnosticCode =
        "application.container.runtimeUnavailable";

    public static bool IsMissing(IContainerApplicationRuntimeHandler? runtimeHandler) =>
        runtimeHandler is null or NoopContainerApplicationRuntimeHandler;

    public static string CreateRuntimeUnavailableReason(Resource resource, ResourceOperationId operationId) =>
        $"Container application resource '{resource.EffectiveResourceId}' cannot execute '{operationId}' because no container application runtime is configured for this host.";

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
                CreateRuntimeUnavailableReason(resource, operationId),
                resource.EffectiveResourceId)
        ]);
}
