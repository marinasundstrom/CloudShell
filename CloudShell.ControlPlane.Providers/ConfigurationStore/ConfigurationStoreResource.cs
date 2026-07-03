namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ConfigurationKind =>
        Resource.Attributes.GetString(ConfigurationStoreResourceTypeProvider.Attributes.Kind);

    public string? Endpoint =>
        Resource.Attributes.GetString(ConfigurationStoreResourceTypeProvider.Attributes.Endpoint);

    public int EntryCount =>
        int.TryParse(
            Resource.Attributes.GetString(ConfigurationStoreResourceTypeProvider.Attributes.EntryCount),
            out var entryCount)
                ? entryCount
                : 0;

    public ValueTask<ConfigurationStoreInspectOperation?> GetInspectOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(ConfigurationStoreResourceTypeProvider.Operations.Inspect)
                as ConfigurationStoreInspectOperation);
}

public sealed class ConfigurationStoreResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => ConfigurationStoreResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == ConfigurationStoreResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new ConfigurationStoreResource(resource));
}
