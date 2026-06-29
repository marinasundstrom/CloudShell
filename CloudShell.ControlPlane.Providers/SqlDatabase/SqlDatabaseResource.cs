namespace CloudShell.ControlPlane.Providers;

public sealed class SqlDatabaseResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? DatabaseName =>
        Resource.Attributes.GetString(
            SqlDatabaseResourceTypeProvider.Attributes.DatabaseName);

    public ResourceReference? OwningServerReference =>
        SqlDatabaseResourceTypeProvider.TryGetServerDependencyResourceId(
            Resource.State,
            out var serverResourceId)
            ? SqlDatabaseResourceTypeProvider.CreateOwningServerReference(serverResourceId)
            : null;

    public string? OwningServerResourceId => OwningServerReference?.Value;

    public string? ServerResourceId => OwningServerResourceId;

    public string? Source =>
        Resource.Attributes.GetString(
            SqlDatabaseResourceTypeProvider.Attributes.Source);

    public bool EnsureCreated =>
        bool.TryParse(
            Resource.Attributes.GetString(
                SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated),
            out var ensureCreated) &&
        ensureCreated;

    public ValueTask<SqlDatabaseEnsureCreatedOperation?> GetEnsureCreatedOperationAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            Resource.Operations.Get(SqlDatabaseResourceTypeProvider.Operations.EnsureCreated)
                as SqlDatabaseEnsureCreatedOperation);
}

public sealed class SqlDatabaseResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => SqlDatabaseResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == SqlDatabaseResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new SqlDatabaseResource(resource));
}
