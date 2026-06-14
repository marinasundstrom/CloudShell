using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.Components.Pages.Resources;

namespace CloudShell.Hosting.ResourceManager;

public sealed class ResourceManagerExtension(bool includeSettings = true) : ICloudShellExtension
{
    public ResourceManagerExtension()
        : this(includeSettings: true)
    {
    }

    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.resource-manager",
        "Resource Manager",
        "A shared resource registry, inventory, lifecycle state, relationships, groups, and endpoints.",
        "0.1.0",
        ["resource-manager.resources", "resource-manager.lifecycle", "resource-manager.relationships"],
        ["shell.commands"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
            .RegisterView<Resources>(ResourceManagerViews.Resources)
            .AddNavigationItem<Resources>("Resources", "server", 10)
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
                "overview",
                "Overview",
                10)
            .AddResourceTab<UpdateLocalStorageResource>(
                "cloudshell.storage",
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true)
            .AddResourceType<RegisterVolumeResource, UpdateVolumeResource>(
                "cloudshell.volume",
                "Volume",
                "Create mountable storage that resources can attach as a volume.",
                "storage",
                8,
                resourceClass: ResourceClass.Storage)
            .AddResourceTab<VolumeOverview>(
                "cloudshell.volume",
                "overview",
                "Overview",
                10)
            .AddResourceTab<UpdateVolumeResource>(
                "cloudshell.volume",
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true)
            .AddResourceType<RegisterLoadBalancerResource>(
                "cloudshell.loadBalancer",
                "Load Balancer",
                "Create provider-backed HTTP, HTTPS, or TCP routes to registered resources.",
                "network",
                9,
                resourceClass: ResourceClass.Network);

        if (includeSettings)
        {
            builder.RegisterView<ResourceManagerSettings>(ResourceManagerViews.ResourceSettings);
        }
    }
}
