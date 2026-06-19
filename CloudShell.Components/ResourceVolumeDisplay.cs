using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public static class ResourceVolumeDisplay
{
    public static string GetAccessModeLabel(VolumeAccessMode mode) =>
        GetAccessModeLabel(mode, static value => value);

    public static string GetAccessModeLabel(VolumeAccessMode mode, Func<string, string> localize) =>
        mode switch
        {
            VolumeAccessMode.ReadOnlyMany => localize("Read-only many"),
            VolumeAccessMode.ReadWriteMany => localize("Read/write many"),
            _ => localize("Read/write once")
        };

    public static string GetAccessModeLabel(string? value) =>
        GetAccessModeLabel(value, static text => text);

    public static string GetAccessModeLabel(string? value, Func<string, string> localize) =>
        Enum.TryParse<VolumeAccessMode>(value, out var mode)
            ? GetAccessModeLabel(mode, localize)
            : GetAccessModeLabel(VolumeAccessMode.ReadWriteOnce, localize);
}
