using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages;
using CloudShell.Hosting.Components.Pages.Settings;
using CloudShell.UI.Composition;
using CloudShell.UI.Composition.Blazor;

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
        builder.Services.AddCloudShellUiCompositionModule(
            ShellCompositionIds.CoreModule,
            module =>
            {
                module.AddPage(ShellCompositionIds.OverviewPage, "Overview", "/");
                module.AddPage(ShellCompositionIds.UsersPage, "Users", "/account/users");
                module.AddPage(ShellCompositionIds.ExtensionsPage, "Extensions", "/extensions");

                module
                    .AddPage(ShellCompositionIds.SettingsPage, "Settings", "/settings/{section?}", isExtendable: true)
                    .AddSections(ShellCompositionIds.SettingsMainOutlet, isExtendable: true)
                    .AddSection<ShellSettingsOverviewSection>(
                        ShellCompositionIds.SettingsOverviewSection,
                        "Overview",
                        10)
                    .AddSection<ShellSettingsPlatformSection>(
                        ShellCompositionIds.SettingsPlatformSection,
                        "Platform",
                        20);

                var mainMenu = module.AddMenu(ShellCompositionIds.MainMenu, "Main");

                mainMenu
                    .AddGroup(ShellCompositionIds.WorkspaceMenuGroup, "Workspace", 10)
                    .AddItem(ShellCompositionIds.OverviewMenuItem, "Overview", 0)
                    .WithAttribute(CompositionAttributeNames.Icon, "grid")
                    .Target(ShellCompositionIds.OverviewPage);

                var platformMenu = mainMenu
                    .AddGroup(ShellCompositionIds.PlatformMenuGroup, "Platform", 70);

                platformMenu
                    .AddItem(ShellCompositionIds.SettingsMenuItem, "Settings", 75)
                    .WithAttribute(CompositionAttributeNames.Icon, "settings")
                    .Target(ShellCompositionIds.SettingsPage);
                platformMenu
                    .AddItem(ShellCompositionIds.UsersMenuItem, "Users", 80)
                    .WithAttribute(CompositionAttributeNames.Icon, "users")
                    .Target(ShellCompositionIds.UsersPage);
                platformMenu
                    .AddItem(ShellCompositionIds.ExtensionsMenuItem, "Extensions", 90)
                    .WithAttribute(CompositionAttributeNames.Icon, "plug")
                    .Target(ShellCompositionIds.ExtensionsPage);
            });

        builder
            .RegisterView<Home>(CoreShellViews.Overview)
            .RegisterView<ShellSettings>(CoreShellViews.Settings)
            .RegisterView<Components.Pages.Account.Users>(CoreShellViews.Users)
            .RegisterView<Components.Pages.Extensions.Extensions>(CoreShellViews.Extensions);
    }
}
