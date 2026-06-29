namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class DockerContainerResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? WorkloadKind =>
        Resource.Attributes.GetString(DockerContainerResourceTypeProvider.Attributes.WorkloadKind);

    public string? Image =>
        Resource.Attributes.GetString(DockerContainerResourceTypeProvider.Attributes.ContainerImage);

    public string? Registry =>
        Resource.Attributes.GetString(DockerContainerResourceTypeProvider.Attributes.ContainerRegistry);

    public int Replicas =>
        int.TryParse(
            Resource.Attributes.GetString(DockerContainerResourceTypeProvider.Attributes.ContainerReplicas),
            out var replicas)
                ? replicas
                : 1;

    public int EndpointCount =>
        int.TryParse(
            Resource.Attributes.GetString(DockerContainerResourceTypeProvider.Attributes.EndpointCount),
            out var endpointCount)
                ? endpointCount
                : 0;

    public bool SupportsMonitoring =>
        Resource.Capabilities.Has(ResourceCommonCapabilityIds.Monitoring);

    public bool SupportsLogSources =>
        Resource.Capabilities.Has(ResourceLogSourceCapabilityIds.LogSources);

    public ValueTask<DockerContainerLifecycleOperation?> GetStartOperationAsync(
        CancellationToken cancellationToken = default) =>
        GetLifecycleOperationAsync(DockerContainerResourceTypeProvider.Operations.Start);

    public ValueTask<DockerContainerLifecycleOperation?> GetStopOperationAsync(
        CancellationToken cancellationToken = default) =>
        GetLifecycleOperationAsync(DockerContainerResourceTypeProvider.Operations.Stop);

    public ValueTask<DockerContainerLifecycleOperation?> GetPauseOperationAsync(
        CancellationToken cancellationToken = default) =>
        GetLifecycleOperationAsync(DockerContainerResourceTypeProvider.Operations.Pause);

    public ValueTask<DockerContainerLifecycleOperation?> GetRestartOperationAsync(
        CancellationToken cancellationToken = default) =>
        GetLifecycleOperationAsync(DockerContainerResourceTypeProvider.Operations.Restart);

    public ValueTask<DockerContainerLifecycleOperation?> GetUnpauseOperationAsync(
        CancellationToken cancellationToken = default) =>
        GetLifecycleOperationAsync(DockerContainerResourceTypeProvider.Operations.Unpause);

    private ValueTask<DockerContainerLifecycleOperation?> GetLifecycleOperationAsync(
        ResourceOperationId operationId) =>
        ValueTask.FromResult(
            Resource.Operations.Get(operationId) as DockerContainerLifecycleOperation);
}

public sealed class DockerContainerResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => DockerContainerResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == DockerContainerResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new DockerContainerResource(resource));
}
