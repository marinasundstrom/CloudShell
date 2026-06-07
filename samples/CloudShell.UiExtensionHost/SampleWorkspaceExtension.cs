using CloudShell.Abstractions.Extensions;
using CloudShell.UiExtensionHost.Pages;

namespace CloudShell.UiExtensionHost;

public sealed class SampleWorkspaceExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "sample.workspace",
        "Sample Workspace",
        "A UI-only extension hosted without the CloudShell control plane.",
        "0.1.0",
        ["sample.ui"],
        ["shell.navigation"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
            .RegisterView<SampleWorkspace>()
            .AddNavigationItem<SampleWorkspace>("Sample workspace", "sparkle", 5)
            .UseStartView<SampleWorkspace>();
    }
}
