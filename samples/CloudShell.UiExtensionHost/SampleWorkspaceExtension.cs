using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Shell;
using CloudShell.UiExtensionHost.Pages;
using CoreShell;

namespace CloudShell.UiExtensionHost;

public sealed class SampleWorkspaceExtension : ICloudShellExtension
{
    private static readonly CoreShellModuleId ModuleId =
        CoreShellModuleId.Create("sample-workspace");

    private static readonly CoreShellPageId SampleWorkspacePage =
        CoreShellPageId.Create("sample-workspace");

    private static readonly CoreShellMenuItemId SampleWorkspaceMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "sample-workspace");

    public CloudShellExtensionManifest Manifest => new(
        "sample.workspace",
        "Sample Workspace",
        "A UI-only extension hosted without the CloudShell control plane.",
        "0.1.0",
        ["sample.ui"],
        ["shell.navigation"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddCoreShellModule(
            ModuleId,
            module =>
            {
                module.AddPage(SampleWorkspacePage, "Sample workspace", "/sample-workspace");

                module
                    .AddMenu(ShellIds.MainMenu, "Main")
                    .AddGroup(ShellIds.WorkspaceMenuGroup, "Workspace", 10)
                    .AddItem(SampleWorkspaceMenuItem, "Sample workspace", 5)
                    .WithAttribute(CoreShellAttributeNames.Icon, "sparkle")
                    .Target(SampleWorkspacePage);
            });

        builder
            .RegisterView<SampleWorkspace>()
            .UseStartView<SampleWorkspace>();
    }
}
