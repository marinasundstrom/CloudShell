using CoreShell;
using CoreShell.Blazor;
using CoreShell.FluentUiSample;
using CoreShell.FluentUiSample.Components;
using CoreShell.FluentUiSample.Components.Layout;
using CoreShell.FluentUiSample.Components.Pages.Sections.Dashboard;
using CoreShell.FluentUiSample.Components.Pages.Sections.Operations;
using CoreShell.FluentUiSample.Components.Pages.Sections.Settings;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration["ReloadStaticAssetsAtRuntime"] ??= "false";
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();
builder.Services.AddCoreShellBlazor();

builder.Services.AddSingleton(CoreShellModule.Create(
    SampleShellIds.HostModule,
    module =>
    {
        module.AddPage(SampleShellIds.DashboardPage, "Dashboard", "/")
            .AddSections(SampleShellIds.DashboardMainOutlet, isExtendable: true)
            .AddSection<SystemOverviewSection>(
                SampleShellIds.SystemOverviewSection,
                "System overview",
                10,
                attributes: SampleShellIds.Icon("data-pie"))
            .AddSection<WorkQueueSection>(
                SampleShellIds.WorkQueueSection,
                "Work queue",
                20,
                attributes: SampleShellIds.Icon("task-list"));

        module.AddPage(SampleShellIds.OperationsPage, "Operations", "/operations")
            .AddSections(SampleShellIds.OperationsMainOutlet)
            .AddSection<OperationsSummarySection>(
                SampleShellIds.OperationsSummarySection,
                "Operations summary",
                10,
                attributes: SampleShellIds.Icon("pulse"))
            .AddSection<IncidentQueueSection>(
                SampleShellIds.IncidentQueueSection,
                "Incident queue",
                20,
                attributes: SampleShellIds.Icon("warning"));

        module.AddPage(SampleShellIds.SettingsPage, "Settings", "/settings/{section?}")
            .AddSections(
                SampleShellIds.SettingsMainOutlet,
                isExtendable: true,
                addressMode: CoreShellSectionAddressMode.Child)
            .AddSection<GeneralSettingsSection>(
                SampleShellIds.GeneralSettingsSection,
                "General",
                10,
                attributes: SampleShellIds.Icon("settings"))
            .AddSection<AppearanceSettingsSection>(
                SampleShellIds.AppearanceSettingsSection,
                "Appearance",
                20,
                attributes: SampleShellIds.Icon("theme"));

        var mainMenu = module.AddMenu(SampleShellIds.MainMenu, "CoreShell Fluent UI");

        mainMenu
            .AddGroup(SampleShellIds.WorkspaceMenuGroup, "Workspace", 10)
            .AddItem(SampleShellIds.DashboardMenuItem, "Dashboard", 10)
            .WithAttribute(CoreShellAttributeNames.Icon, "home")
            .Target(SampleShellIds.DashboardPage);
        mainMenu
            .AddGroup(SampleShellIds.WorkspaceMenuGroup, "Workspace", 10)
            .AddItem(SampleShellIds.OperationsMenuItem, "Operations", 20)
            .WithAttribute(CoreShellAttributeNames.Icon, "pulse")
            .Target(SampleShellIds.OperationsPage);
        mainMenu
            .AddGroup(SampleShellIds.PlatformMenuGroup, "Platform", 20)
            .AddItem(SampleShellIds.SettingsMenuItem, "Settings", 10)
            .WithAttribute(CoreShellAttributeNames.Icon, "settings")
            .Target(SampleShellIds.SettingsPage);
    }));

builder.Services.AddSingleton(CoreShellModule.Create(
    SampleShellIds.ExtensionModule,
    module =>
    {
        module
            .ExtendSections(SampleShellIds.DashboardPage, SampleShellIds.DashboardMainOutlet)
            .AddSection<ExtensionHealthSection>(
                SampleShellIds.ExtensionHealthSection,
                "Extension health",
                30,
                attributes: SampleShellIds.Icon("extension"));

        module
            .AddMenu(SampleShellIds.MainMenu, "CoreShell Fluent UI")
            .AddGroup(SampleShellIds.WorkspaceMenuGroup, "Workspace", 10)
            .AddItem(SampleShellIds.ExtensionHealthMenuItem, "Extension health", 30)
            .WithAttribute(CoreShellAttributeNames.Icon, "extension")
            .Target(SampleShellIds.ExtensionHealthSection);
    }));

builder.Services.TryAddSingleton<CoreShellModuleCatalog>();
builder.Services.TryAddSingleton<ICoreShellPageService>(
    serviceProvider => serviceProvider.GetRequiredService<CoreShellModuleCatalog>());
builder.Services.TryAddSingleton<ICoreShellNavigationService>(
    serviceProvider => serviceProvider.GetRequiredService<CoreShellModuleCatalog>());
builder.Services.TryAddSingleton<ICoreShellSectionService>(
    serviceProvider => serviceProvider.GetRequiredService<CoreShellModuleCatalog>());
builder.Services.TryAddSingleton<ICoreShellRouteService>(
    serviceProvider => serviceProvider.GetRequiredService<CoreShellModuleCatalog>());
builder.Services.TryAddSingleton<ICoreShellSectionAddressService, CoreShellSectionAddressService>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICoreShellPageResolver, CoreShellModuleCatalog>());
builder.Services.TryAddSingleton<ICoreShellPageResolutionService, CoreShellPageResolutionService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
