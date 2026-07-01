using CoreShell.Composition;

namespace CoreShell.Composition.Tests;

public sealed class CompositionEngineHostTests
{
    private static readonly CompositionModuleId WorkspaceModule = new("composition-module.workspace");
    private static readonly CompositionModuleId ReportsModule = new("composition-module.reports");
    private static readonly PageId WorkspacePage = new("page.workspace");
    private static readonly PageId ReportsPage = new("page.reports");
    private static readonly SectionOutletId WorkspaceOutlet = new("section-outlet.workspace.main");
    private static readonly SectionOutletId ReportsOutlet = new("section-outlet.reports.main");
    private static readonly SectionId WorkspaceSection = new("section.workspace.main.overview");
    private static readonly SectionId ReportsSection = new("section.reports.main.summary");

    [Fact]
    public void Mount_AddsModuleArtifactsToActiveRegistry()
    {
        var host = new CompositionEngineHost([CreateWorkspaceModule()]);

        host.Mount(CreateReportsModule());

        Assert.Equal(2, host.Modules.Count);
        Assert.NotNull(host.Registry.GetPage(WorkspacePage));
        Assert.NotNull(host.Registry.GetPage(ReportsPage));
        Assert.Single(host.Registry.GetSections(ReportsPage, ReportsOutlet));
    }

    [Fact]
    public void Mount_DoesNotMutateHostWhenRegistryValidationFails()
    {
        var workspace = CreateWorkspaceModule();
        var host = new CompositionEngineHost([workspace]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            host.Mount(workspace));

        Assert.Contains("Duplicate composition module ID", exception.Message);
        var module = Assert.Single(host.Modules);
        Assert.Equal(WorkspaceModule, module.Id);
        Assert.NotNull(host.Registry.GetPage(WorkspacePage));
        Assert.Null(host.Registry.GetPage(ReportsPage));
    }

    [Fact]
    public void Unmount_RemovesModuleArtifactsFromActiveRegistry()
    {
        var host = new CompositionEngineHost([
            CreateWorkspaceModule(),
            CreateReportsModule()]);

        var removed = host.Unmount(ReportsModule);

        Assert.True(removed);
        var module = Assert.Single(host.Modules);
        Assert.Equal(WorkspaceModule, module.Id);
        Assert.NotNull(host.Registry.GetPage(WorkspacePage));
        Assert.Null(host.Registry.GetPage(ReportsPage));
    }

    [Fact]
    public void Unmount_ReturnsFalseWhenModuleIsNotMounted()
    {
        var host = new CompositionEngineHost([CreateWorkspaceModule()]);

        var removed = host.Unmount(ReportsModule);

        Assert.False(removed);
        Assert.Single(host.Modules);
        Assert.NotNull(host.Registry.GetPage(WorkspacePage));
    }

    private static CompositionModule CreateWorkspaceModule() =>
        CompositionModule.Create(WorkspaceModule, module =>
        {
            module
                .AddPage(WorkspacePage, "Workspace", "/")
                .AddSections(WorkspaceOutlet)
                .AddSection<WorkspaceSectionComponent>(WorkspaceSection, "Overview", 10);
        });

    private static CompositionModule CreateReportsModule() =>
        CompositionModule.Create(ReportsModule, module =>
        {
            module
                .AddPage(ReportsPage, "Reports", "/reports")
                .AddSections(ReportsOutlet)
                .AddSection<ReportsSectionComponent>(ReportsSection, "Summary", 10);
        });

    private sealed class WorkspaceSectionComponent;

    private sealed class ReportsSectionComponent;
}
