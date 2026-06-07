using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed record LocalProcessDefinition(
    string Id,
    string ExecutablePath,
    string? Arguments = null,
    string? WorkingDirectory = null,
    IReadOnlyList<EnvironmentVariableAssignment>? EnvironmentVariables = null,
    LocalProcessLifetime Lifetime = LocalProcessLifetime.Detached);

public enum LocalProcessLifetime
{
    Detached,
    ControlPlaneScoped
}
