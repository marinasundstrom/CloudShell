namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed record ExecutableApplicationConfiguration(
    string Path,
    string? Arguments,
    string? WorkingDirectory = null);
