namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class CloudShellVolumeResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<CloudShellVolumeResourceDefinitionBuilder>(name)
{
    public const StorageVolumeAccessMode DefaultAccessMode = StorageVolumeAccessMode.ReadWriteOnce;

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

    public CloudShellVolumeResourceDefinitionBuilder WithAccessMode(StorageVolumeAccessMode accessMode) =>
        SetScalarAttribute(CloudShellVolumeResourceTypeProvider.Attributes.AccessMode, accessMode.ToString());

    public CloudShellVolumeResourceDefinitionBuilder WithPersistent(bool persistent = true) =>
        SetScalarAttribute(CloudShellVolumeResourceTypeProvider.Attributes.Persistent, persistent);

    public CloudShellVolumeResourceDefinitionBuilder UseLocalFileSystemVolume(
        string? subPath = null,
        StorageVolumeAccessMode accessMode = DefaultAccessMode,
        bool persistent = true)
    {
        WithProvider(StorageResourceDefaults.LocalProvider);
        WithStorageMedium(StorageResourceDefaults.FileSystemMedium);
        WithAccessMode(accessMode);
        WithPersistent(persistent);

        if (!string.IsNullOrWhiteSpace(subPath))
        {
            WithSubPath(subPath);
        }

        return this;
    }

    public CloudShellVolumeResourceDefinitionBuilder UseLocalFileSystemPath(
        string? path = StorageResourceDefaults.DefaultAdHocVolumePath,
        StorageVolumeAccessMode accessMode = DefaultAccessMode,
        bool persistent = true)
    {
        WithProvider(StorageResourceDefaults.LocalProvider);
        WithStorageMedium(StorageResourceDefaults.FileSystemMedium);
        WithAccessMode(accessMode);
        WithPersistent(persistent);

        WithLocation(string.IsNullOrWhiteSpace(path)
            ? StorageResourceDefaults.DefaultAdHocVolumePath
            : path);

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

    public static CloudShellVolumeResourceDefinitionBuilder AddVolume(
        this ResourceDefinitionGraphBuilder graph,
        string name,
        string? path = StorageResourceDefaults.DefaultAdHocVolumePath,
        StorageVolumeAccessMode accessMode = CloudShellVolumeResourceDefinitionBuilder.DefaultAccessMode,
        bool persistent = true)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return graph
            .AddCloudShellVolume(name)
            .UseLocalFileSystemPath(path, accessMode, persistent);
    }

    public static CloudShellVolumeResourceDefinitionBuilder AddVolume(
        this ResourceDefinitionGraphBuilder graph,
        string name,
        IResourceDefinitionBuilder storage,
        string? subPath = null,
        StorageVolumeAccessMode accessMode = CloudShellVolumeResourceDefinitionBuilder.DefaultAccessMode,
        bool persistent = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(storage);

        return graph
            .AddCloudShellVolume(name)
            .UseStorage(storage)
            .UseLocalFileSystemVolume(subPath, accessMode, persistent);
    }
}
