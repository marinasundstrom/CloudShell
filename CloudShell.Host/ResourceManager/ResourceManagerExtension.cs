using CloudShell.Abstractions.Extensions;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Host.Components.Pages.Resources;

namespace CloudShell.Host.ResourceManager;

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
            .AddView<Resources>("Resources", "/resources", "server", 10)
            .AddView<AddResource>("Add resource", "/resources/add", "plus", 11, showInNavigation: false)
            .AddView<CreateResourceGroup>("Create resource group", "/resources/groups/new", "folder", 12, showInNavigation: false)
            .AddResourceProvider<CloudShellResourceProvider>()
            .AddResourceProvider<ManagedResourceProvider>();
    }
}
