namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class CloudShellVolumeResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<CloudShellVolumeResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        CloudShellVolumeResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        CloudShellVolumeResourceTypeProvider.ProviderId;

    public CloudShellVolumeResourceDefinitionBuilder UseStorage(
        IResourceDefinitionBuilder storage)
    {
        ArgumentNullException.ThrowIfNull(storage);

        return UseStorage(storage.EffectiveResourceId);
    }

    public CloudShellVolumeResourceDefinitionBuilder UseStorage(string storageResourceId) =>
        AddDependency(ResourceReference.DependsOnResourceId(
            storageResourceId,
            typeId: StorageResourceTypeProvider.ResourceTypeId));

    public CloudShellVolumeResourceDefinitionBuilder WithProvider(string provider) =>
        SetScalarAttribute(CloudShellVolumeResourceTypeProvider.Attributes.Provider, provider);

    public CloudShellVolumeResourceDefinitionBuilder WithStorageMedium(string medium) =>
        SetScalarAttribute(CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium, medium);

    public CloudShellVolumeResourceDefinitionBuilder WithLocation(string location) =>
        SetScalarAttribute(CloudShellVolumeResourceTypeProvider.Attributes.Location, location);

    public CloudShellVolumeResourceDefinitionBuilder WithSubPath(string subPath) =>
        SetScalarAttribute(CloudShellVolumeResourceTypeProvider.Attributes.SubPath, subPath);

    public CloudShellVolumeResourceDefinitionBuilder WithAccessMode(string accessMode) =>
        SetScalarAttribute(CloudShellVolumeResourceTypeProvider.Attributes.AccessMode, accessMode);

    public CloudShellVolumeResourceDefinitionBuilder WithPersistent(bool persistent = true) =>
        SetScalarAttribute(CloudShellVolumeResourceTypeProvider.Attributes.Persistent, persistent);

    public CloudShellVolumeResourceDefinitionBuilder UseLocalFileSystemVolume(
        string? subPath = null,
        string accessMode = "ReadWriteOnce",
        bool persistent = true)
    {
        WithProvider("Local Storage");
        WithStorageMedium("FileSystem");
        WithAccessMode(accessMode);
        WithPersistent(persistent);

        if (!string.IsNullOrWhiteSpace(subPath))
        {
            WithSubPath(subPath);
        }

        return this;
    }
}

public static class CloudShellVolumeResourceDefinitionBuilderExtensions
{
    public static CloudShellVolumeResourceDefinitionBuilder AddCloudShellVolume(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new CloudShellVolumeResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
