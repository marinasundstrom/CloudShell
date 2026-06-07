using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages;

namespace CloudShell.Hosting.Shell;

public sealed class CoreShellExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.core",
        "CloudShell Core",
        "The host shell, workspace chrome, settings surface, and extension catalog.",
        "0.1.0",
        ["shell.navigation", "shell.commands", "shell.theme"],
        []);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
            .RegisterView<Home>(CoreShellViews.Overview)
            .AddNavigationItem<Home>("overview", "Overview", "grid", 0)
            .RegisterView<Components.Pages.Extensions.Extensions>(CoreShellViews.Extensions)
            .AddNavigationItem<Components.Pages.Extensions.Extensions>("Extensions", "plug", 90, "Platform");
    }
}
