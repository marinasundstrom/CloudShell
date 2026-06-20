using CloudShell.CompositionSandbox;
using CloudShell.CompositionSandbox.Components;
using CloudShell.CompositionSandbox.Components.Pages.Sections;
using CloudShell.UI.Composition.Blazor;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();

builder.Services.AddCloudShellUiComposition(composition =>
{
    var mainMenu = composition.AddMenu(CompositionIds.MainMenu, "Main");

    mainMenu
        .AddItem(CompositionIds.WorkspaceItem, "Workspace", 10)
        .Target(CompositionIds.WorkspacePage);

    var workspaceSection = mainMenu.AddSection(
        CompositionIds.WorkspaceMenuSection,
        "Workspace sections",
        20);

    workspaceSection
        .AddItem(CompositionIds.OverviewSectionItem, "Overview section", 10)
        .Target(CompositionIds.OverviewSection);

    workspaceSection
        .AddItem(CompositionIds.ExtensionSectionItem, "Extension section", 20)
        .Target(CompositionIds.ExtensionContributionSection);

    var workspacePage = composition.AddPage(
        CompositionIds.WorkspacePage,
        "Composition workspace",
        "/");

    var workspaceSections = workspacePage.AddSections(
        CompositionIds.WorkspaceMainOutlet,
        allowExtending: true);

    workspaceSections.AddSection<OverviewSection>(
        CompositionIds.OverviewSection,
        "Composition root",
        10);

    composition
        .GetSections(CompositionIds.WorkspaceMainOutlet)
        .AddSection<ExtensionContributionSection>(
            CompositionIds.ExtensionContributionSection,
            "Contributed section",
            20);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>();

app.Run();
