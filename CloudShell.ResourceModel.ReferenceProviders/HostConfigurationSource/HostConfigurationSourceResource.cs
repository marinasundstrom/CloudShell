namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class HostConfigurationSourceResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? ConfigurationKind =>
        Resource.Attributes.GetString(HostConfigurationSourceResourceTypeProvider.Attributes.ConfigurationKind);

    public string? Source =>
        Resource.Attributes.GetString(HostConfigurationSourceResourceTypeProvider.Attributes.Source);

    public int EntryCount =>
        int.TryParse(
            Resource.Attributes.GetString(HostConfigurationSourceResourceTypeProvider.Attributes.EntryCount),
            out var entryCount)
                ? entryCount
                : 0;

    public ValueTask<HostConfigurationSourceInspectOperation?> GetInspectOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(HostConfigurationSourceResourceTypeProvider.Operations.Inspect)
                as HostConfigurationSourceInspectOperation);
}

public sealed class HostConfigurationSourceResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => HostConfigurationSourceResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == HostConfigurationSourceResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new HostConfigurationSourceResource(resource));
}
