namespace CloudShell.ControlPlane.Providers;

public sealed record ExecutableApplicationConfiguration(
    string Path,
    string? Arguments,
    string? WorkingDirectory = null);
