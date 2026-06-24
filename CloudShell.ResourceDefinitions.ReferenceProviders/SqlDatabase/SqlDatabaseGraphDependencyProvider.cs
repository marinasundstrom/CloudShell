namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SqlDatabaseGraphDependencyProvider : IResourceGraphDependencyProvider
{
    public bool CanResolveDependencies(Resource resource) =>
        resource.Type.TypeId == SqlDatabaseResourceTypeProvider.ResourceTypeId;

    public IEnumerable<ResourceReference> GetDependencies(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return SqlDatabaseResourceTypeProvider.TryGetServerResourceId(
            resource.State,
            out var serverResourceId)
            ? [ResourceReference.ResourceId(
                serverResourceId,
                typeId: SqlServerResourceTypeProvider.ResourceTypeId)]
            : [];
    }
}
