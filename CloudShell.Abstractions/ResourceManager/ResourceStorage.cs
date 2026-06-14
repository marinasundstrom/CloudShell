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
