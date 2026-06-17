namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceTypeContribution(
    string Id,
    string DisplayName,
    string Description,
    string Icon,
    int Order,
    Type RegistrationComponentType,
    Type? UpdateComponentType = null,
    IReadOnlyList<ResourceTabContribution>? Tabs = null,
    IReadOnlyList<ResourcePredefinedViewSectionContribution>? PredefinedViewSections = null,
    ResourceTypeProbeOptions? ProbeOptions = null,
    ResourceClass ResourceClass = ResourceClass.Generic)
{
    public IReadOnlyList<ResourceTabContribution> ResourceTabs => Tabs ?? [];

    public IReadOnlyList<ResourcePredefinedViewSectionContribution> ResourcePredefinedViewSections =>
        PredefinedViewSections ?? [];

    public ResourceTypeProbeOptions ResourceProbeOptions => ProbeOptions ?? ResourceTypeProbeOptions.None;

    public IReadOnlyList<ResourceHealthCheck> ResourceHealthChecks => ResourceProbeOptions.ResourceHealthChecks;
}

public sealed record ResourceTypeProbeOptions(
    IReadOnlyList<ResourceHealthCheck>? HealthChecks = null,
    bool EnableHealthChecksByDefault = true)
{
    public static ResourceTypeProbeOptions None { get; } = new();

    public IReadOnlyList<ResourceHealthCheck> ResourceHealthChecks => HealthChecks ?? [];

    public bool SupportsHealthChecks => ResourceHealthChecks.Count > 0;
}
