using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages;
using CloudShell.Hosting.Components.Pages.Settings;
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
                module
                    .AddPage(ShellCompositionIds.SettingsPage, "Settings", "/settings", isExtendable: true)
                    .AddSections(ShellCompositionIds.SettingsMainOutlet, isExtendable: true)
                    .AddSection<ShellSettingsOverviewSection>(
                        ShellCompositionIds.SettingsOverviewSection,
                        "Overview",
                        10)
                    .AddSection<ShellSettingsPlatformSection>(
                        ShellCompositionIds.SettingsPlatformSection,
                        "Platform",
                        20);
            });

        builder
            .RegisterView<Home>(CoreShellViews.Overview)
            .AddNavigationItem<Home>("overview", "Overview", "grid", 0)
            .RegisterView<ShellSettings>(CoreShellViews.Settings)
            .AddNavigationItem<ShellSettings>("Settings", "settings", 75, "Platform")
            .RegisterView<Components.Pages.Account.Users>(CoreShellViews.Users)
            .AddNavigationItem<Components.Pages.Account.Users>("Users", "users", 80, "Platform")
            .RegisterView<Components.Pages.Extensions.Extensions>(CoreShellViews.Extensions)
            .AddNavigationItem<Components.Pages.Extensions.Extensions>("Extensions", "plug", 90, "Platform");
    }
}
