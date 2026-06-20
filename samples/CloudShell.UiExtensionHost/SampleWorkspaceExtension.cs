using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Shell;
using CloudShell.UiExtensionHost.Pages;
using CloudShell.UI.Composition;
using CloudShell.UI.Composition.Blazor;

namespace CloudShell.UiExtensionHost;

public sealed class SampleWorkspaceExtension : ICloudShellExtension
{
    private static readonly CompositionModuleId ModuleId =
        CompositionModuleId.Create("sample-workspace");

    private static readonly PageId SampleWorkspacePage =
        PageId.Create("sample-workspace");

    private static readonly MenuItemId SampleWorkspaceMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "sample-workspace");

    public CloudShellExtensionManifest Manifest => new(
        "sample.workspace",
        "Sample Workspace",
        "A UI-only extension hosted without the CloudShell control plane.",
        "0.1.0",
        ["sample.ui"],
        ["shell.navigation"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.AddCloudShellUiCompositionModule(
            ModuleId,
            module =>
            {
                module.AddPage(SampleWorkspacePage, "Sample workspace", "/sample-workspace");

                module
                    .GetMenu(ShellCompositionIds.MainMenu)
                    .AddGroup(ShellCompositionIds.WorkspaceMenuGroup, "Workspace", 10)
                    .AddItem(SampleWorkspaceMenuItem, "Sample workspace", 5)
                    .WithAttribute(CompositionAttributeNames.Icon, "sparkle")
                    .Target(SampleWorkspacePage);
            });

        builder
            .RegisterView<SampleWorkspace>()
            .UseStartView<SampleWorkspace>();
    }
}
