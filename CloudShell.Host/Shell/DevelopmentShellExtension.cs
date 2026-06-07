using CloudShell.Abstractions.Extensions;
using CloudShell.Host.Components.Pages.ClickMe;

namespace CloudShell.Host.Shell;

public sealed class DevelopmentShellExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.host.development",
        "CloudShell Host Development",
        "Development-only shell views used by the CloudShell.Host sample.",
        "0.1.0",
        ["shell.development"],
        ["shell.navigation"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
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
                "Click the button to update local component state.");
    }
}
