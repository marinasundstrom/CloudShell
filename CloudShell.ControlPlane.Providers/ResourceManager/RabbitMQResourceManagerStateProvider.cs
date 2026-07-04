using CloudShell.ControlPlane.ResourceModel;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQResourceManagerStateProvider(
    IRabbitMQRuntimeHandler? runtimeHandler = null) :
    IResourceModelResourceManagerStateProvider
{
    private readonly IRabbitMQRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopRabbitMQRuntimeHandler();

    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != RabbitMQResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return _runtimeHandler.GetStatus(resource) switch
        {
            RabbitMQRuntimeStatus.Running => ResourceManagerState.Running,
            RabbitMQRuntimeStatus.Stopped => ResourceManagerState.Stopped,
            _ => ResourceManagerState.Unknown
        };
    }
}
