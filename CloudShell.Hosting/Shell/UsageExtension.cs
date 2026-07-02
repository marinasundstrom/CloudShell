using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Extensions;
using CoreShell;

namespace CloudShell.Hosting.Shell;

public sealed class UsageExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.usage",
        "Usage",
        "Resource usage recording, statistics, and trend projections.",
        "0.1.0",
        ["usage.views", "usage.samples", "usage.statistics"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddCoreShellModule(
            UsageShellIds.Module,
            module =>
            {
                module.AddPage(
                    UsageShellIds.UsagePage,
                    "Usage",
                    "/usage",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(UsageAuthorization.UsageReadPermissions));

                module
                    .AddMenu(ShellIds.MainMenu, "Main")
                    .AddGroup(ShellIds.WorkspaceMenuGroup, "Workspace", 10)
                    .AddItem(UsageShellIds.UsageMenuItem, "Usage", 26)
                    .WithAttribute(CoreShellAttributeNames.Icon, "usage")
                    .RequiresPermissions(UsageAuthorization.UsageReadPermissions)
                    .Target(UsageShellIds.UsagePage);
            });

        builder.RegisterView<Components.Pages.Usage.Usage>(UsageViews.Usage);
    }
}
