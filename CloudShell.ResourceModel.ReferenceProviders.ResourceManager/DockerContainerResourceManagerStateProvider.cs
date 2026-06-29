using CloudShell.ResourceModel.ResourceManager;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ResourceModel.ReferenceProviders.ResourceManager;

public sealed class DockerContainerResourceManagerStateProvider(
    IDockerContainerRuntimeHandler? runtimeHandler = null) :
    IResourceModelResourceManagerStateProvider
{
    private readonly IDockerContainerRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopDockerContainerRuntimeHandler();

    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != DockerContainerResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return _runtimeHandler.GetStatus(resource) switch
        {
            DockerContainerRuntimeStatus.Running => ResourceManagerState.Running,
            DockerContainerRuntimeStatus.Paused => ResourceManagerState.Paused,
            DockerContainerRuntimeStatus.Stopped => ResourceManagerState.Stopped,
            _ => ResourceManagerState.Unknown
        };
    }
}
