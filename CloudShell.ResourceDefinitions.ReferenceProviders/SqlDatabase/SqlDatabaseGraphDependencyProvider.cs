namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SqlDatabaseGraphDependencyProvider : IResourceGraphDependencyProvider
{
    public bool CanResolveDependencies(Resource resource) =>
        resource.Type.TypeId == SqlDatabaseResourceTypeProvider.ResourceTypeId;

    public IEnumerable<ResourceReference> GetDependencies(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return SqlDatabaseResourceTypeProvider.TryGetServerDependencyResourceId(
            resource.State,
            out var serverResourceId)
            ? [ResourceReference.DependsOnResourceId(
                serverResourceId,
                typeId: SqlServerResourceTypeProvider.ResourceTypeId)]
            : [];
    }
}
