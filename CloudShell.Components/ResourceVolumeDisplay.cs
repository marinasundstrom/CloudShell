using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public static class ResourceVolumeDisplay
{
    public static string GetAccessModeLabel(VolumeAccessMode mode) =>
        mode switch
        {
            VolumeAccessMode.ReadOnlyMany => "Read-only many",
            VolumeAccessMode.ReadWriteMany => "Read/write many",
            _ => "Read/write once"
        };

    public static string GetAccessModeLabel(string? value) =>
        Enum.TryParse<VolumeAccessMode>(value, out var mode)
            ? GetAccessModeLabel(mode)
            : GetAccessModeLabel(VolumeAccessMode.ReadWriteOnce);
}
