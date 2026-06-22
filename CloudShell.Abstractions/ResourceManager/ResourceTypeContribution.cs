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
    IReadOnlyList<ResourceEndpointDescriptor>? EndpointDescriptors = null,
    ResourceTypeProbeOptions? ProbeOptions = null,
    ResourceClass ResourceClass = ResourceClass.Generic)
{
    public IReadOnlyList<ResourceTabContribution> ResourceTabs => Tabs ?? [];

    public IReadOnlyList<ResourcePredefinedViewSectionContribution> ResourcePredefinedViewSections =>
        PredefinedViewSections ?? [];

    public IReadOnlyList<ResourceEndpointDescriptor> ResourceEndpointDescriptors =>
        EndpointDescriptors ?? [];

    public ResourceTypeProbeOptions ResourceProbeOptions => ProbeOptions ?? ResourceTypeProbeOptions.None;

    public IReadOnlyList<ResourceHealthCheck> ResourceHealthChecks => ResourceProbeOptions.ResourceHealthChecks;
}

public sealed record ResourceEndpointDescriptor(
    string Name,
    int TargetPort,
    string Protocol = "tcp",
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    ResourceEndpointAssignment DefaultAssignment = ResourceEndpointAssignment.ProviderDefault,
    bool SupportsPortRemapping = true)
{
    public static ResourceEndpointDescriptor Http(
        string name = "http",
        int targetPort = 80,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        ResourceEndpointAssignment defaultAssignment = ResourceEndpointAssignment.ProviderDefault,
        bool supportsPortRemapping = true) =>
        new(name, targetPort, "http", exposure, defaultAssignment, supportsPortRemapping);

    public static ResourceEndpointDescriptor Https(
        string name = "https",
        int targetPort = 443,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        ResourceEndpointAssignment defaultAssignment = ResourceEndpointAssignment.ProviderDefault,
        bool supportsPortRemapping = true) =>
        new(name, targetPort, "https", exposure, defaultAssignment, supportsPortRemapping);

    public static ResourceEndpointDescriptor Tcp(
        string name,
        int targetPort,
        ResourceExposureScope exposure = ResourceExposureScope.Local,
        ResourceEndpointAssignment defaultAssignment = ResourceEndpointAssignment.ProviderDefault,
        bool supportsPortRemapping = true) =>
        new(name, targetPort, "tcp", exposure, defaultAssignment, supportsPortRemapping);
}

public sealed record ResourceTypeProbeOptions(
    IReadOnlyList<ResourceHealthCheck>? HealthChecks = null,
    bool EnableHealthChecksByDefault = true,
    bool SupportsHealth = false)
{
    public static ResourceTypeProbeOptions None { get; } = new();

    public IReadOnlyList<ResourceHealthCheck> ResourceHealthChecks => HealthChecks ?? [];

    public bool SupportsHealthChecks => SupportsHealth || ResourceHealthChecks.Count > 0;
}
