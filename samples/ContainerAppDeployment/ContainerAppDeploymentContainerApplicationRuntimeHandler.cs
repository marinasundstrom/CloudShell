using CloudShell.ResourceModel;
using CloudShell.ResourceModel.ReferenceProviders;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

internal sealed class ContainerAppDeploymentContainerApplicationRuntimeHandler(
    IContainerAppDeploymentContainerApplicationRuntimeBridge bridge) : IContainerApplicationRuntimeHandler
{
    private const string AppResourceId = "application.container-app:sample-api";

    public ContainerApplicationRuntimeStatus GetStatus(ResourceModelResource resource) =>
        IsSampleApp(resource)
            ? bridge.GetStatus(resource)
            : ContainerApplicationRuntimeStatus.Unknown;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSampleApp(resource))
        {
            return [];
        }

        return await bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsSampleApp(resource))
        {
            return [];
        }

        return await bridge.ApplyImageAsync(resource, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsSampleApp(resource))
        {
            return [];
        }

        return await bridge.ApplyReplicasAsync(resource, cancellationToken);
    }

    private static bool IsSampleApp(ResourceModelResource resource) =>
        string.Equals(resource.EffectiveResourceId, AppResourceId, StringComparison.OrdinalIgnoreCase);
}

internal sealed class ContainerAppDeploymentContainerApplicationRuntimeBridge :
    IContainerAppDeploymentContainerApplicationRuntimeBridge
{
    public ContainerApplicationRuntimeStatus GetStatus(ResourceModelResource resource) =>
        ContainerApplicationRuntimeStatus.Stopped;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Information,
                "containerAppDeployment.containerApp.runtimeDeferred",
                $"Container app operation '{operationId}' was accepted without runtime materialization.",
                resource.EffectiveResourceId)
        ]);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Information,
                "containerAppDeployment.containerApp.imageAccepted",
                "Container app image state was accepted without runtime materialization.",
                resource.EffectiveResourceId)
        ]);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Information,
                "containerAppDeployment.containerApp.replicasAccepted",
                "Container app replica state was accepted without runtime materialization.",
                resource.EffectiveResourceId)
        ]);
}
