namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SqlServerResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? Version =>
        Resource.Attributes.GetString(SqlServerResourceTypeProvider.Attributes.Version);

    public string? Edition =>
        Resource.Attributes.GetString(SqlServerResourceTypeProvider.Attributes.Edition);

    public string? ContainerHostResourceId =>
        SqlServerResourceTypeProvider.TryGetContainerHostResourceId(
            Resource.State,
            out var containerHostResourceId)
            ? containerHostResourceId
            : null;

    public IReadOnlyList<SqlServerDatabaseDefinition> Databases =>
        Resource.GetConfiguration<SqlServerConfiguration>(
            SqlServerResourceTypeProvider.ConfigurationSection)?.Databases ?? [];

    public ValueTask<SqlServerReconcileAccessOperation?> GetReconcileAccessOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(SqlServerResourceTypeProvider.Operations.ReconcileAccess)
                as SqlServerReconcileAccessOperation);
}

public sealed class SqlServerResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => SqlServerResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == SqlServerResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new SqlServerResource(resource));
}
