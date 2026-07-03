namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? Version =>
        Resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.Version);

    public bool ManagementUiEnabled =>
        !bool.TryParse(
            Resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.ManagementUi),
            out var enabled) ||
        enabled;

    public string? ContainerHostResourceId =>
        RabbitMQResourceTypeProvider.TryGetContainerHostResourceId(
            Resource.State,
            out var containerHostResourceId)
            ? containerHostResourceId
            : null;

    public IReadOnlyList<NetworkingEndpointRequestValue> EndpointRequests =>
        Resource.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
            RabbitMQResourceTypeProvider.Attributes.EndpointRequests) ?? [];

    public ValueTask<RabbitMQLifecycleOperation?> GetStartOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(RabbitMQResourceTypeProvider.Operations.Start)
                as RabbitMQLifecycleOperation);

    public ValueTask<RabbitMQLifecycleOperation?> GetStopOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(RabbitMQResourceTypeProvider.Operations.Stop)
                as RabbitMQLifecycleOperation);

    public ValueTask<RabbitMQLifecycleOperation?> GetRestartOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(RabbitMQResourceTypeProvider.Operations.Restart)
                as RabbitMQLifecycleOperation);
}

public sealed class RabbitMQResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => RabbitMQResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == RabbitMQResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new RabbitMQResource(resource));
}
