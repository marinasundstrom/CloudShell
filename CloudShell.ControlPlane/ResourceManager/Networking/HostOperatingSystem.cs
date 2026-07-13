namespace CloudShell.ControlPlane.ResourceManager.Networking;

public sealed class HostOperatingSystem(
    bool isMacOS,
    bool isWindows,
    bool isLinux)
{
    public static HostOperatingSystem Current { get; } =
        new(
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsLinux());

    public bool IsMacOS { get; } = isMacOS;

    public bool IsWindows { get; } = isWindows;

    public bool IsLinux { get; } = isLinux;
}
