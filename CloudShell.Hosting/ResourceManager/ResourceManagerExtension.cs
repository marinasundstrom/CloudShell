using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.Components.Pages.Resources;
using CloudShell.Hosting.Shell;
using CloudShell.UI.Composition;

namespace CloudShell.Hosting.ResourceManager;

public sealed class ResourceManagerExtension(bool includeSettings = true) : ICloudShellExtension
{
    private static readonly IReadOnlyDictionary<string, string> ResourceManagementSettingsGroup =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CompositionAttributeNames.Group] = "Resource Management"
        };

    public ResourceManagerExtension()
        : this(includeSettings: true)
    {
    }

    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.resource-manager",
        "Resource Manager",
        "A shared resource registry, inventory, lifecycle state, relationships, groups, and endpoints.",
        "0.1.0",
        ["resource-manager.resources", "resource-manager.lifecycle", "resource-manager.relationships", "resource-manager.health"],
        ["shell.commands"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddCompositionModule(
            ResourceManagerCompositionIds.Module,
            composition =>
            {
                composition.AddPage(
                    ResourceManagerCompositionIds.ResourcesPage,
                    "Resources",
                    ResourceManagerRoutes.Resources);
                composition.AddPage(
                    ResourceManagerCompositionIds.ResourceGraphPage,
                    "Resource graph",
                    ResourceManagerRoutes.ResourceGraph);
                composition.AddPage(
                    ResourceManagerCompositionIds.ResourceDetailsPage,
                    "Resource details",
                    "/resources/{resourceId}/{view?}");
                composition.AddPage(
                    ResourceManagerCompositionIds.HealthPage,
                    "Health",
                    "/health");
                composition.AddPage(
                    ResourceManagerCompositionIds.AddResourcePage,
                    "Add resource",
                    ResourceManagerRoutes.AddResource);
                composition.AddPage(
                    ResourceManagerCompositionIds.CreateResourceGroupPage,
                    "Create resource group",
                    ResourceManagerRoutes.CreateResourceGroup);
                composition.AddPage(
                    ResourceManagerCompositionIds.ResourceTemplatesPage,
                    "Resource templates",
                    ResourceManagerRoutes.ResourceTemplates);
                composition.AddPage(
                    ResourceManagerCompositionIds.ResourceSettingsPage,
                    "Resource Manager settings",
                    ResourceManagerRoutes.ResourceSettings);

                var workspaceMenu = composition
                    .GetMenu(ShellCompositionIds.MainMenu)
                    .AddGroup(ShellCompositionIds.WorkspaceMenuGroup, "Workspace", 10);

                workspaceMenu
                    .AddItem(ResourceManagerCompositionIds.ResourcesMenuItem, "Resources", 10)
                    .WithAttribute(CompositionAttributeNames.Icon, "server")
                    .Target(ResourceManagerCompositionIds.ResourcesPage);
                workspaceMenu
                    .AddItem(ResourceManagerCompositionIds.HealthMenuItem, "Health", 15)
                    .WithAttribute(CompositionAttributeNames.Icon, "health")
                    .Target(ResourceManagerCompositionIds.HealthPage);
            });

        builder
            .RegisterView<Resources>(ResourceManagerViews.Resources)
            .RegisterView<ResourceDependencyGraph>(ResourceManagerViews.ResourceGraph)
            .RegisterView<Health>(ResourceManagerViews.Health)
            .RegisterView<AddResource>(ResourceManagerViews.AddResource)
            .RegisterView<CreateResourceGroup>(ResourceManagerViews.CreateResourceGroup)
            .RegisterView<ResourceTemplates>(ResourceManagerViews.ResourceTemplates)
            .RegisterView<UpdateResource>(ResourceManagerViews.UpdateResource)
            .AddResourceType<RegisterNetworkResource>(
                "cloudshell.network",
                "Network",
                "Create a logical network boundary for orchestrated resources.",
                "network",
                5,
                resourceClass: ResourceClass.Network)
            .AddResourceType<RegisterServiceResource>(
                "cloudshell.service",
                "Service",
                "Create an explicit service unit or facade over one or more target resources.",
                "service",
                6,
                resourceClass: ResourceClass.Service)
            .AddResourceType<RegisterLocalStorageResource, UpdateLocalStorageResource>(
                "cloudshell.storage",
                "Local Storage",
                "Create a filesystem-backed Storage resource.",
                "storage",
                7,
                resourceClass: ResourceClass.Storage)
            .AddResourceTab<LocalStorageOverview>(
                "cloudshell.storage",
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<StorageVolumes>(
                "cloudshell.storage",
                ResourcePredefinedViewIds.Volumes,
                "Volumes",
                20,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourceTab<UpdateLocalStorageResource>(
                "cloudshell.storage",
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                30,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceType<RegisterVolumeResource, UpdateVolumeResource>(
                "cloudshell.volume",
                "Volume",
                "Create mountable storage that resources can attach as a volume.",
                "storage",
                8,
                resourceClass: ResourceClass.Storage)
            .AddResourceTab<VolumeOverview>(
                "cloudshell.volume",
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<UpdateVolumeResource>(
                "cloudshell.volume",
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceType<RegisterLoadBalancerResource, UpdateLoadBalancerResource>(
                "cloudshell.loadBalancer",
                "Load Balancer",
                "Create provider-backed HTTP, HTTPS, or TCP routes to registered resources.",
                "network",
                9,
                resourceClass: ResourceClass.Network)
            .AddResourceType<RegisterDnsZoneResource>(
                "cloudshell.dnsZone",
                "DNS Zone",
                "Create a logical DNS or name-resolution boundary.",
                "network",
                10,
                resourceClass: ResourceClass.Network)
            .AddResourceTab<DnsZoneOverview>(
                "cloudshell.dnsZone",
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceType<RegisterNameMappingResource, UpdateNameMappingResource>(
                "cloudshell.nameMapping",
                "Name Mapping",
                "Create or update a DNS-style name mapping owned by a DNS Zone.",
                "network",
                11,
                resourceClass: ResourceClass.Network);

        if (includeSettings)
        {
            builder.AddCompositionModule<ShellCompositionHostContext>(
                ResourceManagerCompositionIds.SettingsModule,
                (context, composition) =>
                {
                    composition
                        .Extend(context.Settings.MainSections)
                        .AddSection<ResourceManagerSettingsSection>(
                            ResourceManagerCompositionIds.SettingsSection,
                            "Resource Manager",
                            30,
                            ResourceManagementSettingsGroup);
                });

            builder.RegisterView<ResourceManagerSettings>(ResourceManagerViews.ResourceSettings);
        }
    }
}
