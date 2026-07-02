using CloudShell.Abstractions.ResourceManager;
using ResourceOrchestratorSessionAffinityMode = CloudShell.Abstractions.ResourceManager.ResourceOrchestratorSessionAffinityMode;
using ResourceOrchestratorSessionAffinityPolicy = CloudShell.Abstractions.ResourceManager.ResourceOrchestratorSessionAffinityPolicy;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerApplicationResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? Image =>
        Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage);

    public string? Registry =>
        Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry);

    public string? BuildContext =>
        Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerBuildContext);

    public string? Dockerfile =>
        Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerDockerfile);

    public string? ProjectPath =>
        Resource.Attributes.GetString(ResourceAttributeId.Create(ResourceAttributeNames.ProjectPath));

    public int Replicas =>
        int.TryParse(
            Resource.Attributes.GetString(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas),
            out var replicas)
                ? replicas
                : 1;

    public ResourceOrchestratorSessionAffinityPolicy? SessionAffinity =>
        CreateSessionAffinityPolicy(
            Resource.Attributes.GetString(
                ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityMode),
            Resource.Attributes.GetString(
                ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityCookieName),
            Resource.Attributes.GetString(
                ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityDurationSeconds));

    public IReadOnlyList<NetworkingEndpointRequestValue> EndpointRequests =>
        Resource.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
            ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests) ?? [];

    public string? ContainerHostResourceId =>
        ContainerApplicationResourceTypeProvider.TryGetContainerHostResourceId(
            Resource.State,
            out var containerHostResourceId)
            ? containerHostResourceId
            : null;

    public ValueTask<IReadOnlyList<VolumeMountDefinition>> GetVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var volumeConsumer = Resource.Capabilities.Get<VolumeConsumerCapability>();

        return ValueTask.FromResult(volumeConsumer?.Mounts ?? []);
    }

    public ValueTask<ContainerApplicationLifecycleOperation?> GetStartOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ContainerApplicationResourceTypeProvider.Operations.Start)
                as ContainerApplicationLifecycleOperation);

    public ValueTask<ContainerApplicationLifecycleOperation?> GetRestartOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ContainerApplicationResourceTypeProvider.Operations.Restart)
                as ContainerApplicationLifecycleOperation);

    public ValueTask<ContainerApplicationLifecycleOperation?> GetStopOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ContainerApplicationResourceTypeProvider.Operations.Stop)
                as ContainerApplicationLifecycleOperation);

    public ValueTask<ContainerApplicationImageUpdateOperation?> GetImageUpdateOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ContainerApplicationResourceTypeProvider.Operations.UpdateImage)
                as ContainerApplicationImageUpdateOperation);

    private static ResourceOrchestratorSessionAffinityPolicy? CreateSessionAffinityPolicy(
        string? mode,
        string? cookieName,
        string? durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(mode) ||
            !Enum.TryParse<ResourceOrchestratorSessionAffinityMode>(
                mode,
                ignoreCase: true,
                out var parsedMode) ||
            parsedMode == ResourceOrchestratorSessionAffinityMode.None)
        {
            return null;
        }

        var seconds = int.TryParse(durationSeconds, out var parsedSeconds) && parsedSeconds > 0
            ? parsedSeconds
            : (int?)null;
        return parsedMode switch
        {
            ResourceOrchestratorSessionAffinityMode.ClientIp =>
                ResourceOrchestratorSessionAffinityPolicy.ClientIp,
            ResourceOrchestratorSessionAffinityMode.Cookie =>
                ResourceOrchestratorSessionAffinityPolicy.Cookie(
                    string.IsNullOrWhiteSpace(cookieName)
                        ? "CloudShellReplica"
                        : cookieName.Trim(),
                    seconds),
            _ => null
        };
    }
}

public sealed class ContainerApplicationResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => ContainerApplicationResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new ContainerApplicationResource(resource));
}
