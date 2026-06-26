using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ReplicatedContainerHealthGraphStateProvider(
    IApplicationResourceRunningStateOperations runtimeState) : IResourceModelResourceManagerStateProvider
{
    private const string GraphApiResourceId = "application.container-app:graph-api";
    private const string RuntimeApiResourceId = "application:api";

    public ResourceManagerState? GetState(ResourceModelResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.Type.TypeId != ContainerApplicationResourceTypeProvider.ResourceTypeId ||
            !string.Equals(resource.EffectiveResourceId, GraphApiResourceId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return runtimeState.IsRunning(RuntimeApiResourceId)
            ? ResourceManagerState.Running
            : ResourceManagerState.Stopped;
    }
}
