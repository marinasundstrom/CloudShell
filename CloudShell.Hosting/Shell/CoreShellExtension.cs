using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages;
using CloudShell.Hosting.Components.Pages.Settings;
using CoreShell;

namespace CloudShell.Hosting.Shell;

public sealed class CoreShellExtension : ICloudShellExtension
{
    private static readonly IReadOnlyDictionary<string, string> GeneralSettingsGroup =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CoreShellAttributeNames.Group] = "General"
        };

    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.core",
        "CloudShell Core",
        "The host shell, workspace chrome, settings surface, and extension catalog.",
        "0.1.0",
        ["shell.navigation", "shell.commands", "shell.theme"],
        []);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddCoreShellModule(
            ShellIds.CoreModule,
            module =>
            {
                module.AddPage(ShellIds.OverviewPage, "Overview", "/");

                module
                    .AddPage(ShellIds.SettingsPage, "Settings", "/settings/{section?}", isExtendable: true)
                    .AddSections(
                        ShellIds.SettingsMainOutlet,
                        isExtendable: true,
                        addressMode: CoreShellSectionAddressMode.Child)
                    .AddSection<ShellSettingsOverviewSection>(
                        ShellIds.SettingsOverviewSection,
                        "Overview",
                        10,
                        GeneralSettingsGroup)
                    .AddSection<Components.Pages.Account.Users>(
                        ShellIds.SettingsUsersSection,
                        "Users",
                        20,
                        GeneralSettingsGroup)
                    .AddSection<Components.Pages.Extensions.Extensions>(
                        ShellIds.SettingsExtensionsSection,
                        "Extensions",
                        30,
                        GeneralSettingsGroup);

                var mainMenu = module.AddMenu(ShellIds.MainMenu, "Main");

                mainMenu
                    .AddGroup(ShellIds.WorkspaceMenuGroup, "Workspace", 10)
                    .AddItem(ShellIds.OverviewMenuItem, "Overview", 0)
                    .WithAttribute(CoreShellAttributeNames.Icon, "grid")
                    .Target(ShellIds.OverviewPage);
            });

        builder
            .RegisterView<Home>(CoreShellViews.Overview)
            .RegisterView<ShellSettings>(CoreShellViews.Settings)
            .RegisterView<Components.Pages.Account.Users>(CoreShellViews.Users)
            .RegisterView<Components.Pages.Extensions.Extensions>(CoreShellViews.Extensions);
    }
}
