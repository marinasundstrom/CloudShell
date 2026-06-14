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
    VolumeAccessMode AccessMode = VolumeAccessMode.ReadWriteOnce);
