namespace CloudShell.Abstractions.ResourceManager;

public sealed class ResourceRegistrationProbeContext(ResourceTypeContribution resourceType)
{
    public bool EnableHealthChecks { get; set; } =
        resourceType.ResourceProbeOptions.EnableHealthChecksByDefault &&
        resourceType.ResourceProbeOptions.SupportsHealthChecks;

    public bool SupportsHealthChecks => resourceType.ResourceProbeOptions.SupportsHealthChecks;

    public IReadOnlyList<ResourceHealthCheck> GetSelectedHealthChecks() =>
        EnableHealthChecks
            ? resourceType.ResourceHealthChecks
            : [];
}
