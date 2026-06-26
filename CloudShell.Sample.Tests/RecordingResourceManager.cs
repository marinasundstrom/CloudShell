using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.Sample.Tests;

internal sealed class RecordingResourceManager : IResourceManager
{
    public event EventHandler<ResourceChangeNotification>? ResourcesChanged;

    public List<ExecuteResourceActionCommand> ActionCommands { get; } = [];

    public List<UpdateResourceImageCommand> ImageCommands { get; } = [];

    public List<UpdateResourceReplicasCommand> ReplicaCommands { get; } = [];

    public Task<ResourceProcedureResult> ExecuteResourceActionAsync(
        ExecuteResourceActionCommand command,
        CancellationToken cancellationToken = default)
    {
        ActionCommands.Add(command);
        ResourcesChanged?.Invoke(
            this,
            new ResourceChangeNotification(
                ResourceChangeKind.ResourceActionExecuted,
                command.ResourceId));
        return Task.FromResult(ResourceProcedureResult.Completed("executed"));
    }

    public Task<ResourceProcedureResult> UpdateResourceImageAsync(
        UpdateResourceImageCommand command,
        CancellationToken cancellationToken = default)
    {
        ImageCommands.Add(command);
        return Task.FromResult(ResourceProcedureResult.Completed("image updated"));
    }

    public Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
        UpdateResourceReplicasCommand command,
        CancellationToken cancellationToken = default)
    {
        ReplicaCommands.Add(command);
        return Task.FromResult(ResourceProcedureResult.Completed("replicas updated"));
    }

    public Task AssignResourceGroupAsync(
        AssignResourceGroupCommand command,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task CreateResourceAsync(
        CreateResourceCommand command,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResourceGroup> CreateResourceGroupAsync(
        CreateResourceGroupCommand command,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResourceProcedureResult> DeleteResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResourcePermissionEvaluation> EvaluateResourcePermissionGrantAsync(
        ResourceIdentityReference identity,
        string targetResourceId,
        string permission,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResourceGroup?> GetResourceGroupForResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResourceIdentityProvisioningStatusResult> GetResourceIdentityProvisioningStatusAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResourceManagerResource?> GetResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResourceRegistration?> GetResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task GrantResourcePermissionAsync(
        GrantResourcePermissionCommand command,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ResourceManagerResource>> ListAvailableResourcesAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ResourceManagerResource>> ListResourceChildrenAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ResourceGroup>> ListResourceGroupsAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ResourcePermissionGrant>> ListResourcePermissionGrantsAsync(
        ResourcePermissionGrantQuery? query = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ResourcePermissionGrantStatus>> ListResourcePermissionGrantStatusesAsync(
        ResourcePermissionGrantQuery? query = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ResourcePrincipal>> QueryResourcePrincipalsAsync(
        ResourcePrincipalQuery? query = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ResourceRegistration>> ListResourceRegistrationsAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ResourceManagerResource>> ListResourcesAsync(
        ResourceQuery? query = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResourceIdentityProvisioningResult> ProvisionResourceIdentityAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task RegisterResourceAsync(
        RegisterResourceCommand command,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task RemoveResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task RevokeResourcePermissionAsync(
        RevokeResourcePermissionCommand command,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task SetResourceDependenciesAsync(
        SetResourceDependenciesCommand command,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task SetResourceIdentityAsync(
        SetResourceIdentityCommand command,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ResourceIdentityProviderSetupResult> SetupResourceIdentityProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
