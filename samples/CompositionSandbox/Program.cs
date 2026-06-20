using CloudShell.CompositionSandbox;
using CloudShell.CompositionSandbox.Components;
using CloudShell.CompositionSandbox.Components.Pages.Sections.Dashboard;
using CloudShell.CompositionSandbox.Components.Pages.Sections;
using CloudShell.CompositionSandbox.Components.Pages.Sections.Reports;
using CloudShell.CompositionSandbox.Components.Pages.Sections.Settings;
using CloudShell.UI.Composition;
using CloudShell.UI.Composition.Blazor;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

builder.Services.AddCloudShellUiCompositionModule(CompositionModuleId.Host, composition =>
{
    var mainMenu = composition.AddMenu(CompositionIds.MainMenu, "Main");

    mainMenu
        .AddItem(CompositionIds.WorkspaceItem, "Workspace", 10)
        .Target(CompositionIds.WorkspacePage);
    mainMenu
        .AddItem(CompositionIds.ReportsItem, "Reports", 20)
        .Target(CompositionIds.ReportsPage);
    mainMenu
        .AddItem(CompositionIds.DashboardItem, "Dashboard", 30)
        .Target(CompositionIds.DashboardPage);
    mainMenu
        .AddItem(CompositionIds.SettingsItem, "Settings", 40)
        .Target(CompositionIds.SettingsPage);

    var workspaceGroup = mainMenu.AddGroup(
        CompositionIds.WorkspaceMenuGroup,
        "Workspace sections",
        20);

    workspaceGroup
        .AddItem(CompositionIds.OverviewSectionItem, "Overview section", 10)
        .Target(CompositionIds.OverviewSection);

    workspaceGroup
        .AddItem(CompositionIds.ExtensionSectionItem, "Extension section", 20)
        .WithParent(CompositionIds.OverviewSectionItem)
        .Target(CompositionIds.ExtensionContributionSection);

    var workspacePage = composition.AddPage(
        CompositionIds.WorkspacePage,
        "Composition workspace",
        "/");

    var workspaceSections = workspacePage.AddSections(
        CompositionIds.WorkspaceMainOutlet,
        isExtendable: true);

    workspaceSections.AddSection<OverviewSection>(
        CompositionIds.OverviewSection,
        "Composition root",
        10);

    composition
        .AddPage(
            CompositionIds.ReportsPage,
            "Composition reports",
            "/reports")
        .AddSections(CompositionIds.ReportsMainOutlet)
        .AddSection<ReportsSummarySection>(
            CompositionIds.ReportsSummarySection,
            "Page navigation",
            10);

    composition
        .AddPage(
            CompositionIds.DashboardPage,
            "Composition dashboard",
            "/dashboard")
        .AddSections(CompositionIds.DashboardMainOutlet)
        .AddSection<CompositionStatusSection>(
            CompositionIds.DashboardStatusSection,
            "Composition status",
            10)
        .AddSection<ActivitySummarySection>(
            CompositionIds.DashboardActivitySection,
            "Activity summary",
            20)
        .AddSection<LayoutPatternSection>(
            CompositionIds.DashboardLayoutPatternSection,
            "Layout pattern",
            30);

    composition
        .AddPage(
            CompositionIds.SettingsPage,
            "Composition settings",
            "/settings")
        .AddSections(CompositionIds.SettingsMainOutlet)
        .AddSection<GeneralSettingsSection>(
            CompositionIds.SettingsGeneralSection,
            "General",
            10)
        .AddSection<AdvancedSettingsSection>(
            CompositionIds.SettingsAdvancedSection,
            "Advanced",
            20);
});

builder.Services.AddCloudShellUiCompositionModule(
    CompositionModuleId.Create("sample-extension"),
    composition =>
    {
        composition
            .Extend(CompositionIds.WorkspaceMainSections)
            .AddSection<ExtensionContributionSection>(
                CompositionIds.ExtensionContributionSection,
                "Contributed section",
                20);
    });

builder.Services.AddCloudShellUiComposition();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>();

app.Run();
