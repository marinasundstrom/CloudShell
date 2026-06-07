using CloudShell.Abstractions.Extensions;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting.Components.Pages.Resources;

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
        builder
            .RegisterView<Resources>()
            .AddNavigationItem<Resources>("Resources", "server", 10)
            .RegisterView<AddResource>()
            .RegisterView<CreateResourceGroup>()
            .RegisterView<ResourceTemplates>()
            .AddResourceProvider<CloudShellResourceProvider>()
            .AddResourceProvider<ManagedResourceProvider>();
    }
}
