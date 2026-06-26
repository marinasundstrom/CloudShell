namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class StorageResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<StorageResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        StorageResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        StorageResourceTypeProvider.ProviderId;

    public StorageResourceDefinitionBuilder WithProvider(string provider) =>
        SetScalarAttribute(StorageResourceTypeProvider.Attributes.Provider, provider);

    public StorageResourceDefinitionBuilder WithMedium(string medium) =>
        SetScalarAttribute(StorageResourceTypeProvider.Attributes.Medium, medium);

    public StorageResourceDefinitionBuilder WithLocation(string location) =>
        SetScalarAttribute(StorageResourceTypeProvider.Attributes.Location, location);

    public StorageResourceDefinitionBuilder UseLocalFileSystem(string? location = null)
    {
        WithProvider("Local Storage");
        WithMedium("FileSystem");

        if (!string.IsNullOrWhiteSpace(location))
        {
            WithLocation(location);
        }

        return this;
    }
}

public static class StorageResourceDefinitionBuilderExtensions
{
    public static StorageResourceDefinitionBuilder AddStorage(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new StorageResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
