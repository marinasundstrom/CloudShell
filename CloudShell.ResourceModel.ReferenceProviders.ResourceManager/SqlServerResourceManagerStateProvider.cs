using CloudShell.ResourceModel.ResourceManager;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ResourceModel.ReferenceProviders.ResourceManager;

public sealed class SqlServerResourceManagerStateProvider(
    ISqlServerRuntimeHandler? runtimeHandler = null) :
    IResourceModelResourceManagerStateProvider
{
    private readonly ISqlServerRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopSqlServerRuntimeHandler();

    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != SqlServerResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return _runtimeHandler.GetStatus(resource) switch
        {
            SqlServerRuntimeStatus.Running => ResourceManagerState.Running,
            SqlServerRuntimeStatus.Stopped => ResourceManagerState.Stopped,
            _ => ResourceManagerState.Unknown
        };
    }
}
