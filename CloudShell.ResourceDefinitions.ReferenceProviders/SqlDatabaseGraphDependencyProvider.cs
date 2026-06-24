namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SqlDatabaseGraphDependencyProvider : IResourceGraphDependencyProvider
{
    public bool CanResolveDependencies(Resource resource) =>
        resource.Type.TypeId == SqlDatabaseResourceTypeProvider.ResourceTypeId;

    public IEnumerable<ResourceReference> GetDependencies(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var serverResourceId = resource.Attributes.GetString(
            SqlDatabaseResourceTypeProvider.Attributes.ServerResourceId);

        return string.IsNullOrWhiteSpace(serverResourceId)
            ? []
            : [ResourceReference.ResourceId(serverResourceId)];
    }
}
