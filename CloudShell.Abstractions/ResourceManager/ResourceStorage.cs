namespace CloudShell.Abstractions.ResourceManager;

public enum VolumeAccessMode
{
    ReadWriteOnce,
    ReadOnlyMany,
    ReadWriteMany
}

public sealed record VolumeResourceDefinition(
    string Id,
    string Name,
    string? Provider = null,
    string? Location = null,
    bool Persistent = true,
    VolumeAccessMode AccessMode = VolumeAccessMode.ReadWriteOnce,
    string? StorageResourceId = null,
    string? SubPath = null);

public sealed record StorageResourceDefinition(
    string Id,
    string Name,
    string Provider = StorageProviderNames.LocalStorage,
    string Medium = StorageMedia.FileSystem,
    string? Location = null);

public static class StorageProviderNames
{
    public const string LocalStorage = "Local Storage";
}

public static class StorageMedia
{
    public const string FileSystem = "FileSystem";
}

public sealed record ResourceVolumeMountMaterialization(
    string VolumeReference,
    string TargetPath,
    string Source,
    bool ReadOnly,
    string Status = ResourceVolumeMountMaterializationStatus.Materialized,
    string? Reason = null,
    DateTimeOffset? ObservedAt = null);

public static class ResourceVolumeMountMaterializationStatus
{
    public const string Materialized = "materialized";
    public const string NotActive = "notActive";
}

public interface IResourceVolumeMountMaterializationStore
{
    IReadOnlyList<ResourceVolumeMountMaterialization> GetVolumeMountMaterializations(
        string resourceId);

    void SaveVolumeMountMaterializations(
        string resourceId,
        IReadOnlyList<ResourceVolumeMountMaterialization> materializations);
}
