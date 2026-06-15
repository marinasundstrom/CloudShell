using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationVolumeMountInput(
    string? volumeReference = null,
    string? targetPath = null,
    bool readOnly = false)
{
    public string? VolumeReference { get; set; } = volumeReference;

    public string? TargetPath { get; set; } = targetPath;

    public bool ReadOnly { get; set; } = readOnly;

    public static ApplicationVolumeMountInput FromMount(ResourceVolumeMount mount) =>
        new(mount.VolumeReference, mount.TargetPath, mount.ReadOnly);
}
