using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.ControlPlane;

public static class ResourceManagerClientExtensions
{
    public static async Task<ResourceOperationCapabilities> GetResourceOperationCapabilitiesAsync(
        this IResourceManager resourceManager,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);

        var capabilities = await resourceManager.GetResourceOperationCapabilitiesAsync(
            [resourceId],
            cancellationToken);
        return capabilities.GetValueOrDefault(resourceId) ??
            ResourceOperationCapabilities.None(resourceId);
    }

    public static Task<ResourceOperationCapabilities> GetResourceOperationCapabilitiesAsync(
        this IResourceManager resourceManager,
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resourceManager.GetResourceOperationCapabilitiesAsync(
            resource.Id,
            cancellationToken);
    }

    public static Task<ResourceProcedureResult> ExecuteResourceActionAsync(
        this IResourceManager resourceManager,
        string resourceId,
        string actionId,
        bool startDependencies = false,
        bool ignoreDependentWarning = false,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default,
        ResourceIdentityReference? actingIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);

        return resourceManager.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                resourceId,
                actionId,
                startDependencies,
                ignoreDependentWarning,
                triggeredBy,
                actingIdentity),
            cancellationToken);
    }

    public static Task<ResourceProcedureResult> ExecuteResourceActionAsync(
        this IResourceManager resourceManager,
        Resource resource,
        ResourceAction action,
        bool startDependencies = false,
        bool ignoreDependentWarning = false,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default,
        ResourceIdentityReference? actingIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(action);

        return resourceManager.ExecuteResourceActionAsync(
            resource.Id,
            action.Id,
            startDependencies,
            ignoreDependentWarning,
            triggeredBy,
            cancellationToken,
            actingIdentity);
    }

    public static Task<ResourceProcedureResult> UpdateResourceImageAsync(
        this IResourceManager resourceManager,
        string resourceId,
        string image,
        bool restartIfRunning = true,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);

        return resourceManager.UpdateResourceImageAsync(
            new UpdateResourceImageCommand(resourceId, image, restartIfRunning, triggeredBy),
            cancellationToken);
    }

    public static Task<ResourceProcedureResult> UpdateResourceImageAsync(
        this IResourceManager resourceManager,
        Resource resource,
        string image,
        bool restartIfRunning = true,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resourceManager.UpdateResourceImageAsync(
            resource.Id,
            image,
            restartIfRunning,
            triggeredBy,
            cancellationToken);
    }

    public static Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
        this IResourceManager resourceManager,
        string resourceId,
        int replicas,
        bool restartIfRunning = true,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);

        return resourceManager.UpdateResourceReplicasAsync(
            new UpdateResourceReplicasCommand(resourceId, replicas, restartIfRunning, triggeredBy),
            cancellationToken);
    }

    public static Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
        this IResourceManager resourceManager,
        Resource resource,
        int replicas,
        bool restartIfRunning = true,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resourceManager.UpdateResourceReplicasAsync(
            resource.Id,
            replicas,
            restartIfRunning,
            triggeredBy,
            cancellationToken);
    }

    public static Task<ResourceProcedureResult> StartResourceAsync(
        this IResourceManager resourceManager,
        string resourceId,
        bool startDependencies = false,
        CancellationToken cancellationToken = default) =>
        resourceManager.ExecuteResourceActionAsync(
            resourceId,
            ResourceActionIds.Start,
            startDependencies,
            cancellationToken: cancellationToken);

    public static Task<ResourceProcedureResult> StartResourceAsync(
        this IResourceManager resourceManager,
        Resource resource,
        bool startDependencies = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resourceManager.StartResourceAsync(
            resource.Id,
            startDependencies,
            cancellationToken);
    }

    public static Task<ResourceProcedureResult> StopResourceAsync(
        this IResourceManager resourceManager,
        string resourceId,
        bool ignoreDependentWarning = false,
        CancellationToken cancellationToken = default) =>
        resourceManager.ExecuteResourceActionAsync(
            resourceId,
            ResourceActionIds.Stop,
            ignoreDependentWarning: ignoreDependentWarning,
            cancellationToken: cancellationToken);

    public static Task<ResourceProcedureResult> StopResourceAsync(
        this IResourceManager resourceManager,
        Resource resource,
        bool ignoreDependentWarning = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resourceManager.StopResourceAsync(
            resource.Id,
            ignoreDependentWarning,
            cancellationToken);
    }

    public static Task<ResourceProcedureResult> PauseResourceAsync(
        this IResourceManager resourceManager,
        string resourceId,
        CancellationToken cancellationToken = default) =>
        resourceManager.ExecuteResourceActionAsync(
            resourceId,
            ResourceActionIds.Pause,
            cancellationToken: cancellationToken);

    public static Task<ResourceProcedureResult> PauseResourceAsync(
        this IResourceManager resourceManager,
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resourceManager.PauseResourceAsync(
            resource.Id,
            cancellationToken);
    }

    public static Task<ResourceProcedureResult> RestartResourceAsync(
        this IResourceManager resourceManager,
        string resourceId,
        bool startDependencies = false,
        bool ignoreDependentWarning = false,
        CancellationToken cancellationToken = default) =>
        resourceManager.ExecuteResourceActionAsync(
            resourceId,
            ResourceActionIds.Restart,
            startDependencies,
            ignoreDependentWarning,
            cancellationToken: cancellationToken);

    public static Task<ResourceProcedureResult> RestartResourceAsync(
        this IResourceManager resourceManager,
        Resource resource,
        bool startDependencies = false,
        bool ignoreDependentWarning = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resourceManager.RestartResourceAsync(
            resource.Id,
            startDependencies,
            ignoreDependentWarning,
            cancellationToken);
    }
}
