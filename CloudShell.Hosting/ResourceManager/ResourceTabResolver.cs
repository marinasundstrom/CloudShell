using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.Components.Pages.Resources;

namespace CloudShell.Hosting.ResourceManager;

internal sealed record ResourceTabResolution(
    IReadOnlyList<ResourceTabContribution> Tabs,
    IReadOnlySet<ResourceViewId> GeneratedViewIds,
    IReadOnlySet<ResourceViewId> ResourceTypeContextViewIds)
{
    public bool IsGenerated(ResourceViewId id) => GeneratedViewIds.Contains(id);

    public bool AcceptsResourceTypeContext(ResourceViewId id) =>
        ResourceTypeContextViewIds.Contains(id);
}

internal static class ResourceTabResolver
{
    public static ResourceTabResolution Resolve(
        Resource? resource,
        ResourceTypeContribution? resourceType,
        bool hasResourceLogs,
        bool hasResourceMonitoring,
        bool canReadUsage,
        bool hasDefaultIdentityProvider)
    {
        var contributedTabs = resourceType?.ResourceTabs ?? [];
        var generatedTabs = new List<ResourceTabContribution>();

        AddGeneratedTab(
            generatedTabs,
            contributedTabs,
            ResourcePredefinedViewIds.Overview,
            "Overview",
            -100,
            typeof(GeneratedResourceOverview),
            ResourceTabGroupTitles.General);

        if (ResourcePredefinedViewVisibility.HasEndpointsView(resource))
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Endpoints,
                "Endpoints",
                -80,
                typeof(GeneratedResourceEndpoints),
                ResourceTabGroupTitles.Networking);
        }

        if (ResourcePredefinedViewVisibility.HasDnsView(resource))
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Dns,
                "DNS",
                -70,
                typeof(GeneratedResourceDns),
                ResourceTabGroupTitles.Networking);
        }

        if (ResourcePredefinedViewVisibility.HasIdentityView(resource, hasDefaultIdentityProvider))
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Identity,
                "Identity",
                82,
                typeof(GeneratedResourceIdentity),
                ResourceTabGroupTitles.Management);
        }

        if (ResourcePredefinedViewVisibility.HasAccessControlView(resource, hasDefaultIdentityProvider))
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.AccessControl,
                "Access control",
                85,
                typeof(GeneratedResourceAccessControl),
                ResourceTabGroupTitles.Management);
        }

        if (ResourcePredefinedViewVisibility.HasStorageVolumesView(resource))
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Volumes,
                "Volumes",
                20,
                typeof(StorageVolumes),
                ResourceTabGroupTitles.Storage);
        }

        AddGeneratedTab(
            generatedTabs,
            contributedTabs,
            ResourcePredefinedViewIds.Activity,
            "Activity",
            90,
            typeof(ResourceActivity),
            ResourceTabGroupTitles.Management);

        if (ResourcePredefinedViewVisibility.HasHealthView(resource, resourceType))
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Health,
                "Health",
                78,
                typeof(GeneratedResourceHealth),
                ResourceTabGroupTitles.Management);
        }

        if (ResourcePredefinedViewVisibility.HasRecoveryView(resource))
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Recovery,
                "Recovery",
                79,
                typeof(GeneratedResourceRecovery),
                ResourceTabGroupTitles.Management);
        }

        if (hasResourceMonitoring)
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Monitoring,
                "Monitoring",
                80,
                typeof(GeneratedResourceMonitoring),
                ResourceTabGroupTitles.Management);
        }

        if (canReadUsage)
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Usage,
                "Usage",
                81,
                typeof(GeneratedResourceUsage),
                ResourceTabGroupTitles.Management);
        }

        if (resourceType?.UpdateComponentType is not null)
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                0,
                resourceType.UpdateComponentType,
                ResourceTabGroupTitles.General,
                showsApplyButton: true);
        }

        if (ResourcePredefinedViewVisibility.HasEnvironmentView(resource))
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Environment,
                "Environment",
                80,
                typeof(ResourceEnvironmentVariables),
                ResourceTabGroupTitles.Management,
                showsApplyButton: true);
        }

        if (hasResourceLogs)
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Logs,
                "Logs",
                70,
                typeof(Components.Pages.Logs.ResourceLogs),
                ResourceTabGroupTitles.Telemetry);
        }

        if (resource?.EffectiveObservability.Traces == true)
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Traces,
                "Traces",
                80,
                typeof(Components.Pages.Observability.ResourceTraces),
                ResourceTabGroupTitles.Telemetry);
        }

        if (resource?.EffectiveObservability.Metrics == true)
        {
            AddGeneratedTab(
                generatedTabs,
                contributedTabs,
                ResourcePredefinedViewIds.Metrics,
                "Metrics",
                90,
                typeof(Components.Pages.Observability.ResourceMetrics),
                ResourceTabGroupTitles.Telemetry);
        }

        var generatedViewIds = generatedTabs.Select(tab => tab.Id).ToHashSet();
        return new ResourceTabResolution(
            generatedTabs.Concat(contributedTabs).ToArray(),
            generatedViewIds,
            generatedViewIds.Where(AcceptsResourceTypeContext).ToHashSet());
    }

    private static void AddGeneratedTab(
        ICollection<ResourceTabContribution> generatedTabs,
        IReadOnlyList<ResourceTabContribution> contributedTabs,
        ResourceViewId id,
        string title,
        int order,
        Type componentType,
        string groupTitle,
        bool showsApplyButton = false)
    {
        if (contributedTabs.Any(tab => tab.Id == id))
        {
            return;
        }

        generatedTabs.Add(new ResourceTabContribution(
            id,
            title,
            order,
            componentType,
            showsApplyButton,
            groupTitle,
            GetPredefinedViewIcon(id)));
    }

    private static string? GetPredefinedViewIcon(ResourceViewId id) =>
        ResourcePredefinedViews.TryGet(id, out var definition)
            ? definition.Icon
            : null;

    private static bool AcceptsResourceTypeContext(ResourceViewId id) =>
        id == ResourcePredefinedViewIds.Identity ||
        id == ResourcePredefinedViewIds.AccessControl ||
        id == ResourcePredefinedViewIds.Activity ||
        id == ResourcePredefinedViewIds.Health ||
        id == ResourcePredefinedViewIds.Recovery ||
        id == ResourcePredefinedViewIds.Monitoring ||
        id == ResourcePredefinedViewIds.Usage ||
        id == ResourcePredefinedViewIds.Endpoints ||
        id == ResourcePredefinedViewIds.Dns;
}
