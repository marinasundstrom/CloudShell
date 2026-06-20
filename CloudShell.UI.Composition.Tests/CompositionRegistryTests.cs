using CloudShell.UI.Composition;

namespace CloudShell.UI.Composition.Tests;

public sealed class CompositionRegistryTests
{
    private static readonly MenuId MainMenu = new("menu.main");
    private static readonly MenuSectionId WorkspaceMenuSection = new("menu-section.main.workspace");
    private static readonly MenuItemId WorkspaceMenuItem = new("menu-item.main.workspace");
    private static readonly MenuItemId SectionMenuItem = new("menu-item.main.workspace.section");
    private static readonly PageId WorkspacePage = new("page.workspace");
    private static readonly PageId ReportsPage = new("page.reports");
    private static readonly SectionOutletId MainOutlet = new("section-outlet.workspace.main");
    private static readonly SectionOutletId ReportsOutlet = new("section-outlet.reports.main");
    private static readonly SectionId OverviewSection = new("section.workspace.main.overview");
    private static readonly SectionId DetailsSection = new("section.workspace.main.details");
    private static readonly SectionId ReportsSummarySection = new("section.reports.main.summary");
    private static readonly CompositionModuleId ReportsModule = new("composition-module.reports");

    [Fact]
    public void GetPageByRoute_NormalizesRouteQueryAndFragment()
    {
        var registry = CreateRegistry();

        var page = registry.GetPageByRoute("workspace?tab=details#section.workspace.main.details");

        Assert.NotNull(page);
        Assert.Equal(WorkspacePage, page.Id);
    }

    [Fact]
    public void ResolveHref_ResolvesPageTargetsWithRouteParameters()
    {
        var registry = CreateRegistry();

        var href = registry.ResolveHref(
            WorkspacePage,
            new Dictionary<string, object?>
            {
                ["view"] = "summary",
                ["empty"] = null
            });

        Assert.Equal("/workspace?view=summary", href);
    }

    [Fact]
    public void ResolveHref_ResolvesSectionTargetsToPageFragment()
    {
        var registry = CreateRegistry();

        var href = registry.ResolveHref(DetailsSection);

        Assert.Equal("/workspace#section.workspace.main.details", href);
    }

    [Fact]
    public void ResolveHref_ResolvesSectionTargetsWithRouteParametersBeforeFragment()
    {
        var registry = CreateRegistry();

        var href = registry.ResolveHref(
            DetailsSection,
            new Dictionary<string, object?>
            {
                ["section"] = DetailsSection.Value,
                ["empty"] = null
            });

        Assert.Equal("/workspace?section=section.workspace.main.details#section.workspace.main.details", href);
    }

    [Fact]
    public void GetSections_OrdersByOrderThenTitle()
    {
        var registry = CreateRegistry();

        var sections = registry.GetSections(WorkspacePage, MainOutlet);

        Assert.Collection(
            sections,
            section => Assert.Equal(DetailsSection, section.Id),
            section => Assert.Equal(OverviewSection, section.Id));
    }

    [Fact]
    public void GetSections_PreservesSectionMetadata()
    {
        var registry = CreateRegistry();

        var details = registry.GetSections(WorkspacePage, MainOutlet)
            .Single(section => section.Id == DetailsSection);

        Assert.Equal(WorkspacePage, details.PageId);
        Assert.Equal(MainOutlet, details.OutletId);
        Assert.Equal("Details", details.Title);
        Assert.Equal(typeof(DetailsSectionComponent), details.ComponentType);
        Assert.Equal(10, details.Order);
    }

    [Fact]
    public void Create_RejectsDuplicatePageIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.Create(composition =>
            {
                composition.AddPage(WorkspacePage, "Workspace", "/workspace");
                composition.AddPage(WorkspacePage, "Workspace duplicate", "/workspace-copy");
            }));

        Assert.Contains("Duplicate composition page ID", exception.Message);
    }

    [Fact]
    public void Create_RejectsDuplicateSectionIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.Create(composition =>
            {
                var page = composition.AddPage(WorkspacePage, "Workspace", "/workspace");
                page
                    .AddSections(MainOutlet)
                    .AddSection<OverviewSectionComponent>(OverviewSection, "Overview", 10)
                    .AddSection<DetailsSectionComponent>(OverviewSection, "Duplicate overview", 20);
            }));

        Assert.Contains("Duplicate composition section ID", exception.Message);
    }

    [Fact]
    public void GetSections_RequiresExtendableOutlet()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.Create(composition =>
            {
                var page = composition.AddPage(WorkspacePage, "Workspace", "/workspace");
                page.AddSections(MainOutlet);

                composition.GetSections(MainOutlet);
            }));

        Assert.Contains("not registered as extendable", exception.Message);
    }

    [Fact]
    public void AddMenu_RegistersItemsAndSections()
    {
        var registry = CreateRegistry();

        var menu = registry.GetMenu(MainMenu);

        Assert.NotNull(menu);
        Assert.Equal("Main", menu.Title);
        var item = Assert.Single(menu.Items);
        Assert.Equal(WorkspaceMenuItem, item.Id);
        Assert.Equal(WorkspacePage.Value, item.Target.Value);
        var section = Assert.Single(menu.Sections);
        Assert.Equal(WorkspaceMenuSection, section.Id);
        var sectionItem = Assert.Single(section.Items);
        Assert.Equal(SectionMenuItem, sectionItem.Id);
        Assert.Equal(DetailsSection.Value, sectionItem.Target.Value);
    }

    [Fact]
    public void Create_AssignsDefaultHostModule()
    {
        var registry = CreateRegistry();

        var module = Assert.Single(registry.Modules);

        Assert.Equal(CompositionModuleId.Host, module.Id);
        Assert.Single(module.Pages);
        Assert.Single(module.Menus);
        Assert.Equal(2, module.Sections.Count);
    }

    [Fact]
    public void FromModules_CombinesModuleArtifacts()
    {
        var host = CreateHostModule();
        var reports = CompositionModule.Create(ReportsModule, module =>
        {
            module
                .AddPage(ReportsPage, "Reports", "/reports")
                .AddSections(ReportsOutlet)
                .AddSection<ReportsSectionComponent>(ReportsSummarySection, "Summary", 10);
        });

        var registry = CompositionRegistry.FromModules(host, reports);

        Assert.Equal(2, registry.Modules.Count);
        Assert.NotNull(registry.GetPage(WorkspacePage));
        Assert.NotNull(registry.GetPage(ReportsPage));
        Assert.Single(registry.GetSections(ReportsPage, ReportsOutlet));
    }

    [Fact]
    public void FromModules_RejectsDuplicateModuleIds()
    {
        var module = CreateHostModule();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.FromModules(module, module));

        Assert.Contains("Duplicate composition module ID", exception.Message);
    }

    private static CompositionRegistry CreateRegistry() =>
        CompositionRegistry.Create(ConfigureHostModule);

    private static CompositionModule CreateHostModule() =>
        CompositionModule.Create(CompositionModuleId.Host, ConfigureHostModule);

    private static void ConfigureHostModule(CompositionBuilder composition)
    {
        var menu = composition.AddMenu(MainMenu, "Main");
        menu
            .AddItem(WorkspaceMenuItem, "Workspace", 10)
            .Target(WorkspacePage);
        menu
            .AddSection(WorkspaceMenuSection, "Workspace sections", 20)
            .AddItem(SectionMenuItem, "Details", 10)
            .Target(DetailsSection);

        var page = composition.AddPage(WorkspacePage, "Workspace", "/workspace");
        page
            .AddSections(MainOutlet, allowExtending: true)
            .AddSection<OverviewSectionComponent>(OverviewSection, "Overview", 10)
            .AddSection<DetailsSectionComponent>(DetailsSection, "Details", 10);
    }

    private static void ConfigureHostModule(CompositionModuleBuilder composition)
    {
        var menu = composition.AddMenu(MainMenu, "Main");
        menu
            .AddItem(WorkspaceMenuItem, "Workspace", 10)
            .Target(WorkspacePage);
        menu
            .AddSection(WorkspaceMenuSection, "Workspace sections", 20)
            .AddItem(SectionMenuItem, "Details", 10)
            .Target(DetailsSection);

        var page = composition.AddPage(WorkspacePage, "Workspace", "/workspace");
        page
            .AddSections(MainOutlet, allowExtending: true)
            .AddSection<OverviewSectionComponent>(OverviewSection, "Overview", 10)
            .AddSection<DetailsSectionComponent>(DetailsSection, "Details", 10);
    }

    private sealed class OverviewSectionComponent;

    private sealed class DetailsSectionComponent;

    private sealed class ReportsSectionComponent;
}
