using CloudShell.ResourceModel.ResourceManager;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ResourceModel.ReferenceProviders.ResourceManager;

public sealed class ContainerApplicationResourceManagerStateProvider(
    IContainerApplicationRuntimeHandler? runtimeHandler = null) :
    IResourceModelResourceManagerStateProvider
{
    private readonly IContainerApplicationRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopContainerApplicationRuntimeHandler();

    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != ContainerApplicationResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return _runtimeHandler.GetStatus(resource) switch
        {
            ContainerApplicationRuntimeStatus.Running => ResourceManagerState.Running,
            ContainerApplicationRuntimeStatus.Stopped => ResourceManagerState.Stopped,
            _ => ResourceManagerState.Unknown
        };
    }
}
