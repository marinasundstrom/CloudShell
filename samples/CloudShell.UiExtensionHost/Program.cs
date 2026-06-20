using CloudShell.Abstractions.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.UiExtensionHost;
using CloudShell.UiExtensionHost.Composition;
using CloudShell.UiExtensionHost.Pages.Sections;

var builder = CloudShellApplication.CreateBuilder(args);

builder.Services.AddSingleton(SampleCompositionRegistry.Create(composition =>
{
    var mainMenu = composition.AddMenu(CompositionIds.MainMenu, "Main");

    mainMenu
        .AddItem(CompositionIds.WorkspaceItem, "Sample workspace", 10)
        .Target(CompositionIds.SampleWorkspacePage);

    var workspaceSection = mainMenu.AddSection(
        CompositionIds.WorkspaceSection,
        "Workspace sections",
        20);

    workspaceSection
        .AddItem(CompositionIds.ExtensionSectionItem, "Extension section", 10)
        .Target(CompositionIds.ExtensionContributionSection);

    var workspacePage = composition.AddPage(
        CompositionIds.SampleWorkspacePage,
        "Sample workspace",
        "/sample-workspace");

    var workspaceSections = workspacePage.AddSections(
        CompositionIds.WorkspaceMainOutlet,
        allowExtending: true);

    workspaceSections.AddSection<ShellSurfaceSection>(
        CompositionIds.ShellSurfaceSection,
        "Embedded shell surface",
        10);

    composition
        .GetSections(CompositionIds.WorkspaceMainOutlet)
        .AddSection<ExtensionContributionSection>(
            CompositionIds.ExtensionContributionSection,
            "Extension contributed section",
            20);
}));

builder
    .AddCloudShellUi()
    .AddExtension<SampleWorkspaceExtension>();

var app = builder.Build();

await app.UseCloudShellUiAsync();
app.MapCloudShellUi<App>();

app.Run();
