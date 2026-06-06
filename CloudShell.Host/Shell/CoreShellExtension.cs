using CloudShell.Abstractions.Extensions;
using CloudShell.Host.Components.Pages;
using CloudShell.Host.Components.Pages.ClickMe;

namespace CloudShell.Host.Shell;

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
            .AddCustomView(
                "cloudshell.click-me",
                "Click me",
                "/click-me",
                "pulse",
                10,
                description: "A simple shell view contributed through the CloudShell extension model.")
            .AddCustomViewMenuItem<ClickMeCounter>(
                "cloudshell.click-me",
                "counter",
                "Counter",
                10,
                "Click the button to update local component state.")
            .AddView<Components.Pages.Extensions.Extensions>("Extensions", "/extensions", "plug", 90, "Platform");
    }
}
