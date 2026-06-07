using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting.Components.Pages.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Hosting.ResourceManager;

public sealed class ResourceManagerExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.resource-manager",
        "Resource Manager",
        "A shared resource registry, inventory, lifecycle state, relationships, groups, and endpoints.",
        "0.1.0",
        ["resource-manager.resources", "resource-manager.lifecycle", "resource-manager.relationships"],
        ["shell.commands"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.TryAddSingleton(new PlatformResourceOptions());
        builder.Services.TryAddSingleton<PlatformResourceStore>();
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<PlatformResourceProvider>());

        builder
            .RegisterView<Resources>(ResourceManagerViews.Resources)
            .AddNavigationItem<Resources>("Resources", "server", 10)
            .RegisterView<AddResource>(ResourceManagerViews.AddResource)
            .RegisterView<CreateResourceGroup>(ResourceManagerViews.CreateResourceGroup)
            .RegisterView<ResourceTemplates>(ResourceManagerViews.ResourceTemplates)
            .RegisterView<ResourceManagerSettings>()
            .RegisterView<UpdateResource>(ResourceManagerViews.UpdateResource)
            .AddResourceType<RegisterNetworkResource>(
                PlatformResourceProvider.NetworkResourceType,
                "Network",
                "Create a logical network boundary for orchestrated resources.",
                "network",
                5)
            .AddResourceType<RegisterServiceResource>(
                PlatformResourceProvider.ServiceResourceType,
                "Service",
                "Create a stable internal or public endpoint over one or more resources.",
                "service",
                6)
            .AddResourceProvider<PlatformResourceProvider>()
            .AddResourceProvider<CloudShellResourceProvider>()
            .AddResourceProvider<ManagedResourceProvider>();
    }
}
