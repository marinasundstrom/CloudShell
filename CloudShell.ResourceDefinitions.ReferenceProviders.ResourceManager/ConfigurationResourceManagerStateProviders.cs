using CloudShell.ResourceDefinitions.ResourceManager;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;

public sealed class ConfigurationStoreResourceManagerStateProvider(
    IConfigurationStoreRuntimeController runtimeController) :
    IResourceModelResourceManagerStateProvider
{
    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != ConfigurationStoreResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return runtimeController.GetStatus(resource) switch
        {
            ResourceWebAppRuntimeStatus.Running => ResourceManagerState.Running,
            ResourceWebAppRuntimeStatus.Stopped => ResourceManagerState.Stopped,
            _ => ResourceManagerState.Unknown
        };
    }
}

public sealed class SecretsVaultResourceManagerStateProvider(
    ISecretsVaultRuntimeController runtimeController) :
    IResourceModelResourceManagerStateProvider
{
    public ResourceManagerState? GetState(Resource resource)
    {
        if (resource.Type.TypeId != SecretsVaultResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return runtimeController.GetStatus(resource) switch
        {
            ResourceWebAppRuntimeStatus.Running => ResourceManagerState.Running,
            ResourceWebAppRuntimeStatus.Stopped => ResourceManagerState.Stopped,
            _ => ResourceManagerState.Unknown
        };
    }
}
