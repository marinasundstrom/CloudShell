using CloudShell.ResourceModel;
using CloudShell.ResourceModel.ReferenceProviders;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

internal interface IContainerAppDeploymentContainerApplicationRuntimeBridge
{
    ContainerApplicationRuntimeStatus GetStatus(ResourceModelResource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken = default);
}
