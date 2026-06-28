namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class StorageResourceDefinitionBuilder(
    string name,
    ResourceDefinitionGraphBuilder? graph = null) :
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
        WithProvider(StorageResourceDefaults.LocalProvider);
        WithMedium(StorageResourceDefaults.FileSystemMedium);

        if (!string.IsNullOrWhiteSpace(location))
        {
            WithLocation(location);
        }

        return this;
    }

    public CloudShellVolumeResourceDefinitionBuilder AddVolume(
        string name,
        string? subPath = null,
        StorageVolumeAccessMode accessMode = StorageVolumeAccessMode.ReadWriteOnce,
        bool persistent = true)
    {
        if (graph is null)
        {
            throw new InvalidOperationException(
                "The storage builder is not attached to a resource graph builder.");
        }

        return graph.AddVolume(
            name,
            this,
            subPath,
            accessMode,
            persistent);
    }
}

public static class StorageResourceDefinitionBuilderExtensions
{
    public static StorageResourceDefinitionBuilder AddStorage(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new StorageResourceDefinitionBuilder(name, graph);
        graph.Add(builder);
        return builder;
    }

    public static StorageResourceDefinitionBuilder AddLocalStorage(
        this ResourceDefinitionGraphBuilder graph,
        string name,
        string? location = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return graph
            .AddStorage(name)
            .UseLocalFileSystem(location);
    }
}
