using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ReplicatedContainerHealthContainerAppRuntimeHandler(
    IReplicatedContainerHealthContainerAppRuntimeBridge bridge) : IContainerApplicationRuntimeHandler
{
    private const string ApiResourceId = "application.container-app:api";

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
        IsApiResource(resource)
            ? bridge.GetStatus(resource)
            : ContainerApplicationRuntimeStatus.Unknown;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.ApplyImageAsync(resource, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsApiResource(resource))
        {
            return [];
        }

        return await bridge.ApplyReplicasAsync(resource, cancellationToken);
    }

    private static bool IsApiResource(GraphResource resource) =>
        string.Equals(resource.EffectiveResourceId, ApiResourceId, StringComparison.OrdinalIgnoreCase);
}
