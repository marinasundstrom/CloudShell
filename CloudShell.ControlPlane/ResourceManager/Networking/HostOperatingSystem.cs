namespace CloudShell.ControlPlane.ResourceManager.Networking;

public sealed class HostOperatingSystem
{
    public HostOperatingSystem(
        HostOperatingSystemKind kind,
        string? linuxDistributionId = null)
    {
        Kind = kind;
        LinuxDistributionId = string.IsNullOrWhiteSpace(linuxDistributionId)
            ? null
            : linuxDistributionId.Trim().ToLowerInvariant();
    }

    public HostOperatingSystem(
        bool isMacOS,
        bool isWindows,
        bool isLinux)
        : this(GetKind(isMacOS, isWindows, isLinux))
    {
    }

    public static HostOperatingSystem Current { get; } = DetectCurrent();

    public HostOperatingSystemKind Kind { get; }

    public string? LinuxDistributionId { get; }

    public bool IsMacOS => Kind == HostOperatingSystemKind.MacOS;

    public bool IsWindows => Kind == HostOperatingSystemKind.Windows;

    public bool IsLinux => Kind == HostOperatingSystemKind.Linux;

    public string DisplayName =>
        IsLinux && !string.IsNullOrWhiteSpace(LinuxDistributionId)
            ? $"linux/{LinuxDistributionId}"
            : Kind.ToString().ToLowerInvariant();

    private static HostOperatingSystem DetectCurrent()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new HostOperatingSystem(HostOperatingSystemKind.MacOS);
        }

        if (OperatingSystem.IsWindows())
        {
            return new HostOperatingSystem(HostOperatingSystemKind.Windows);
        }

        if (OperatingSystem.IsLinux())
        {
            return new HostOperatingSystem(
                HostOperatingSystemKind.Linux,
                TryReadLinuxDistributionId());
        }

        return new HostOperatingSystem(HostOperatingSystemKind.Unknown);
    }

    private static HostOperatingSystemKind GetKind(
        bool isMacOS,
        bool isWindows,
        bool isLinux)
    {
        if (isMacOS)
        {
            return HostOperatingSystemKind.MacOS;
        }

        if (isWindows)
        {
            return HostOperatingSystemKind.Windows;
        }

        if (isLinux)
        {
            return HostOperatingSystemKind.Linux;
        }

        return HostOperatingSystemKind.Unknown;
    }

    private static string? TryReadLinuxDistributionId()
    {
        const string osReleasePath = "/etc/os-release";
        try
        {
            if (!File.Exists(osReleasePath))
            {
                return null;
            }

            foreach (var line in File.ReadLines(osReleasePath))
            {
                if (!line.StartsWith("ID=", StringComparison.Ordinal))
                {
                    continue;
                }

                return line["ID=".Length..].Trim().Trim('"', '\'');
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}

public enum HostOperatingSystemKind
{
    Unknown,
    MacOS,
    Windows,
    Linux
}
