using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal interface IContainerAppDeploymentGraphContainerApplicationRuntimeBridge
{
    ContainerApplicationRuntimeStatus GetStatus(GraphResource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default);
}
