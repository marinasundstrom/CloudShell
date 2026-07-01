using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages;
using CloudShell.Hosting.Components.Pages.Settings;
using CoreShell.Composition;

namespace CloudShell.Hosting.Shell;

public sealed class CoreShellExtension : ICloudShellExtension
{
    private static readonly IReadOnlyDictionary<string, string> GeneralSettingsGroup =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CompositionAttributeNames.Group] = "General"
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
        builder.AddCompositionModule(
            ShellCompositionIds.CoreModule,
            module =>
            {
                module.AddPage(ShellCompositionIds.OverviewPage, "Overview", "/");

                module
                    .AddPage(ShellCompositionIds.SettingsPage, "Settings", "/settings/{section?}", isExtendable: true)
                    .AddSections(ShellCompositionIds.SettingsMainOutlet, isExtendable: true)
                    .UseChildAddresses()
                    .AddSection<ShellSettingsOverviewSection>(
                        ShellCompositionIds.SettingsOverviewSection,
                        "Overview",
                        10,
                        GeneralSettingsGroup)
                    .AddSection<Components.Pages.Account.Users>(
                        ShellCompositionIds.SettingsUsersSection,
                        "Users",
                        20,
                        GeneralSettingsGroup)
                    .AddSection<Components.Pages.Extensions.Extensions>(
                        ShellCompositionIds.SettingsExtensionsSection,
                        "Extensions",
                        30,
                        GeneralSettingsGroup);

                var mainMenu = module.AddMenu(ShellCompositionIds.MainMenu, "Main");

                mainMenu
                    .AddGroup(ShellCompositionIds.WorkspaceMenuGroup, "Workspace", 10)
                    .AddItem(ShellCompositionIds.OverviewMenuItem, "Overview", 0)
                    .WithAttribute(CompositionAttributeNames.Icon, "grid")
                    .Target(ShellCompositionIds.OverviewPage);
            });

        builder
            .RegisterView<Home>(CoreShellViews.Overview)
            .RegisterView<ShellSettings>(CoreShellViews.Settings)
            .RegisterView<Components.Pages.Account.Users>(CoreShellViews.Users)
            .RegisterView<Components.Pages.Extensions.Extensions>(CoreShellViews.Extensions);
    }
}
