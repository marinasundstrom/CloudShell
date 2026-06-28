using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ReplicatedContainerHealthGraphRuntimeHandler(
    IReplicatedContainerHealthGraphContainerAppRuntimeBridge bridge) : IContainerApplicationRuntimeHandler
{
    private const string ApiResourceId = "application.container-app:api";

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
        IsGraphApi(resource)
            ? bridge.GetStatus(resource)
            : ContainerApplicationRuntimeStatus.Unknown;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApi(resource))
        {
            return [];
        }

        return await bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApi(resource))
        {
            return [];
        }

        return await bridge.ApplyImageAsync(resource, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApi(resource))
        {
            return [];
        }

        return await bridge.ApplyReplicasAsync(resource, cancellationToken);
    }

    private static bool IsGraphApi(GraphResource resource) =>
        string.Equals(resource.EffectiveResourceId, ApiResourceId, StringComparison.OrdinalIgnoreCase);
}
