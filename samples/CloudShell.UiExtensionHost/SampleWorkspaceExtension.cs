using CloudShell.Abstractions.Extensions;
using CloudShell.UiExtensionHost.Pages;
using CloudShell.UI.Composition;
using CloudShell.UI.Composition.Blazor;

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
        var samplePage = PageId.Create("sample-workspace");

        builder.Services.AddCloudShellUiCompositionModule(
            CompositionModuleId.Create("sample-workspace"),
            module =>
            {
                module.AddPage(samplePage, "Sample workspace", "/sample-workspace");
            });

        builder
            .RegisterView<SampleWorkspace>()
            .AddNavigationItem<SampleWorkspace>("Sample workspace", "sparkle", 5)
            .UseStartView<SampleWorkspace>();
    }
}
