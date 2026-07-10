using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.Components.Pages.Resources;
using CloudShell.Hosting.Shell;
using CoreShell;

namespace CloudShell.Hosting.ResourceManager;

public sealed class ResourceManagerExtension(bool includeSettings = true) : ICloudShellExtension
{
    private static readonly IReadOnlyDictionary<string, string> ResourceManagementSettingsGroup =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CoreShellAttributeNames.Group] = "Resource Management"
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
        builder.AddCoreShellModule(
            ResourceManagerShellIds.Module,
            module =>
            {
                module.AddPage(
                    ResourceManagerShellIds.ResourcesPage,
                    "Resources",
                    ResourceManagerRoutes.Resources);
                module.AddPage(
                    ResourceManagerShellIds.ResourceGraphPage,
                    "Resource graph",
                    ResourceManagerRoutes.ResourceGraph);
                module.AddPage(
                    ResourceManagerShellIds.EnvironmentPage,
                    "Environment",
                    ResourceManagerRoutes.Environment);
                module.AddPage(
                    ResourceManagerShellIds.ResourceDetailsPage,
                    "Resource details",
                    "/resources/{resourceId}/{view?}");
                module.AddPage(
                    ResourceManagerShellIds.HealthPage,
                    "Health",
                    "/health");
                module.AddPage(
                    ResourceManagerShellIds.AddResourcePage,
                    "Add resource",
                    ResourceManagerRoutes.AddResource);
                module.AddPage(
                    ResourceManagerShellIds.CreateResourceGroupPage,
                    "Create resource group",
                    ResourceManagerRoutes.CreateResourceGroup);
                module.AddPage(
                    ResourceManagerShellIds.ResourceTemplatesPage,
                    "Resource templates",
                    ResourceManagerRoutes.ResourceTemplates);
                module.AddPage(
                    ResourceManagerShellIds.ResourceSettingsPage,
                    "Resource Manager settings",
                    ResourceManagerRoutes.ResourceSettings);

                var workspaceMenu = module
                    .AddMenu(ShellIds.MainMenu, "Main")
                    .AddGroup(ShellIds.WorkspaceMenuGroup, "Workspace", 10);

                workspaceMenu
                    .AddItem(ResourceManagerShellIds.ResourcesMenuItem, "Resources", 10)
                    .WithAttribute(CoreShellAttributeNames.Icon, "server")
                    .Target(ResourceManagerShellIds.ResourcesPage);
                workspaceMenu
                    .AddItem(ResourceManagerShellIds.EnvironmentMenuItem, "Environment", 15)
                    .WithAttribute(CoreShellAttributeNames.Icon, "environment")
                    .Target(ResourceManagerShellIds.EnvironmentPage);
                workspaceMenu
                    .AddItem(ResourceManagerShellIds.HealthMenuItem, "Health", 20)
                    .WithAttribute(CoreShellAttributeNames.Icon, "health")
                    .Target(ResourceManagerShellIds.HealthPage);
            });

        builder
            .RegisterView<Resources>(ResourceManagerViews.Resources)
            .RegisterView<ResourceDependencyGraph>(ResourceManagerViews.ResourceGraph)
            .RegisterView<EnvironmentPage>(ResourceManagerViews.Environment)
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
                resourceClass: ResourceClass.Network)
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                "application.aspnet-core-project",
                ".NET Web App",
                "Create an application resource from an uploaded .NET artifact.",
                "web",
                20,
                resourceClass: ResourceClass.Project)
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                "application.python-app",
                "Python App",
                "Create an application resource from an uploaded Python artifact.",
                "application",
                21,
                resourceClass: ResourceClass.Project)
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                "application.java-app",
                "Java App",
                "Create an application resource from an uploaded Java artifact.",
                "application",
                22,
                resourceClass: ResourceClass.Project)
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                "application.javascript-app",
                "JavaScript App",
                "Create an application resource from an uploaded JavaScript artifact.",
                "application",
                23,
                resourceClass: ResourceClass.Project)
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                "application.go-app",
                "Go App",
                "Create an application resource from an uploaded Go artifact.",
                "application",
                24,
                resourceClass: ResourceClass.Project);

        if (includeSettings)
        {
            builder.AddCoreShellModule<ShellHostContext>(
                ResourceManagerShellIds.SettingsModule,
                (context, module) =>
                {
                    module
                        .Extend(context.Settings.MainSections)
                        .AddSection<ResourceManagerSettingsGeneralSection>(
                            ResourceManagerShellIds.SettingsGeneralSection,
                            "General",
                            30,
                            ResourceManagementSettingsGroup)
                        .AddSection<ResourceManagerSettingsOrchestrationSection>(
                            ResourceManagerShellIds.SettingsOrchestrationSection,
                            "Orchestration",
                            40,
                            ResourceManagementSettingsGroup);
                });

            builder.RegisterView<ResourceManagerSettings>(ResourceManagerViews.ResourceSettings);
        }
    }
}
