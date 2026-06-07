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
            .AddView<Home>("Overview", "/", "grid", 0)
            .AddView<Components.Pages.Extensions.Extensions>("Extensions", "/extensions", "plug", 90, "Platform");
    }
}
