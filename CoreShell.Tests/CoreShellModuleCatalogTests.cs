using CoreShell;

namespace CoreShell.Tests;

public sealed class CoreShellModuleCatalogTests
{
    private static readonly CoreShellModuleId ShellModule = CoreShellModuleId.Create("shell");
    private static readonly CoreShellModuleId ExtensionModule = CoreShellModuleId.Create("extension");
    private static readonly CoreShellMenuId MainMenu = CoreShellMenuId.Create("main");
    private static readonly CoreShellMenuGroupId WorkspaceGroup = CoreShellMenuGroupId.Create(MainMenu, "workspace");
    private static readonly CoreShellPageId HomePage = CoreShellPageId.Create("home");
    private static readonly CoreShellPageId ToolsPage = CoreShellPageId.Create("tools");
    private static readonly CoreShellSectionOutletId ToolsMain = CoreShellSectionOutletId.Create(ToolsPage, "main");
    private static readonly CoreShellSectionId ToolsOverview = CoreShellSectionId.Create(ToolsMain, "overview");
    private static readonly CoreShellPageId SettingsPage = CoreShellPageId.Create("settings");
    private static readonly CoreShellSectionOutletId SettingsMain = CoreShellSectionOutletId.Create(SettingsPage, "main");
    private static readonly CoreShellSectionId SettingsUsers = CoreShellSectionId.Create(SettingsMain, "users");

    [Fact]
    public async Task Services_MergeModulesWithoutExposingCompositionPrimitives()
    {
        var catalog = new CoreShellModuleCatalog([CreateShellModule(), CreateExtensionModule()]);

        var pages = await catalog.GetPagesAsync();
        var menu = await catalog.GetMenuAsync(MainMenu);
        var outlets = await catalog.GetSectionOutletsAsync(ToolsPage);
        var sections = await catalog.GetSectionsAsync(ToolsMain);

        Assert.Equal([HomePage, ToolsPage, SettingsPage], pages.Select(page => page.Id));
        Assert.NotNull(menu);
        var group = Assert.Single(menu.Groups);
        Assert.Equal(WorkspaceGroup, group.Id);
        Assert.Equal(["Home", "Settings", "Tools"], group.Items.Select(item => item.Title));
        Assert.Equal([ToolsMain], outlets.Select(outlet => outlet.Id));
        Assert.Equal([ToolsOverview], sections.Select(section => section.Id));
        Assert.Equal(ToolsOverview, sections[0].Id);
    }

    [Fact]
    public async Task NavigationService_ReturnsMenusWithoutCompositionPrimitives()
    {
        ICoreShellNavigationService navigation = new CoreShellModuleCatalog(
            [CreateShellModule(), CreateExtensionModule()]);

        var menus = await navigation.GetMenusAsync();

        var menu = Assert.Single(menus);
        Assert.Equal(MainMenu, menu.Id);
        Assert.Equal(["Home", "Settings", "Tools"], menu.Groups.Single().Items.Select(item => item.Title));
    }

    [Fact]
    public async Task RouteService_MatchesExactAndTemplatedPages()
    {
        ICoreShellRouteService routes = new CoreShellModuleCatalog(
            [CreateShellModule(), CreateExtensionModule()]);

        var tools = await routes.GetPageByRouteAsync("/tools?filter=active");
        var settings = await routes.GetPageByRouteAsync("/settings/users");
        var settingsRoot = await routes.GetPageByRouteAsync("/settings");

        Assert.Equal(ToolsPage, tools?.Id);
        Assert.Equal(SettingsPage, settings?.Id);
        Assert.Equal(SettingsPage, settingsRoot?.Id);
    }

    [Fact]
    public async Task RouteService_ResolvesPageSectionAndHrefTargets()
    {
        ICoreShellRouteService routes = new CoreShellModuleCatalog(
            [CreateShellModule(), CreateExtensionModule()]);

        var page = await routes.ResolveTargetAsync(
            CoreShellTarget.ForPage(SettingsPage),
            new Dictionary<string, object?>
            {
                ["section"] = "extensions",
                ["mode"] = "compact"
            });
        var section = await routes.ResolveTargetAsync(
            CoreShellTarget.ForSection(SettingsUsers),
            new Dictionary<string, object?>
            {
                ["mode"] = "compact"
            });
        var href = await routes.ResolveTargetAsync(
            CoreShellTarget.ForHref("/external/docs"),
            new Dictionary<string, object?>
            {
                ["from"] = "menu"
            });

        Assert.Equal("/settings/extensions?mode=compact", page.Href);
        Assert.Equal(SettingsPage, page.Page?.Id);
        Assert.Equal("/settings/users?mode=compact", section.Href);
        Assert.Equal(SettingsUsers, section.Section?.Id);
        Assert.Equal("/external/docs?from=menu", href.Href);
    }

    [Fact]
    public async Task PageResolver_MaterializesStaticPageSections()
    {
        ICoreShellPageResolver resolver = new CoreShellModuleCatalog(
            [CreateShellModule(), CreateExtensionModule()]);

        var resolved = await resolver.ResolvePageAsync(new CoreShellPageResolutionContext("/settings/users"));

        Assert.NotNull(resolved);
        Assert.Equal(SettingsPage, resolved.Page.Id);
        Assert.Equal([SettingsMain], resolved.SectionOutlets.Select(outlet => outlet.Id));
        Assert.Equal([SettingsUsers], resolved.Sections.Select(section => section.Id));
    }

    [Fact]
    public async Task PageResolutionService_CanMaterializeDynamicPageWithoutStaticModule()
    {
        var resolver = new CoreShellPageResolutionService(
            [new DynamicPageResolver("/dynamic")]);

        var resolved = await resolver.ResolvePageAsync(new CoreShellPageResolutionContext("/dynamic"));

        Assert.NotNull(resolved);
        Assert.Equal(CoreShellPageId.Create("dynamic"), resolved.Page.Id);
        Assert.Equal("/dynamic", resolved.Page.Route);
        Assert.Empty(resolved.SectionOutlets);
        Assert.Empty(resolved.Sections);
    }

    [Fact]
    public void Constructor_RejectsDuplicatePages()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new CoreShellModuleCatalog([CreateShellModule(), CreateShellModule()]));

        Assert.Contains("Duplicate CoreShell page ID 'home'", exception.Message);
    }

    private static CoreShellModule CreateShellModule() =>
        CoreShellModule.Create(ShellModule, module =>
        {
            module.AddPage(HomePage, "Home", "/");
            module
                .AddMenu(MainMenu, "Main")
                .AddGroup(WorkspaceGroup, "Workspace", 10)
                .AddItem(CoreShellMenuItemId.Create(WorkspaceGroup, "home"), "Home", 0)
                .Target(HomePage);
            module
                .AddMenu(MainMenu, "Main")
                .AddGroup(WorkspaceGroup, "Workspace", 10)
                .AddItem(CoreShellMenuItemId.Create(WorkspaceGroup, "settings"), "Settings", 5)
                .Target(SettingsPage);
        });

    private static CoreShellModule CreateExtensionModule() =>
        CoreShellModule.Create(ExtensionModule, module =>
        {
            module
                .AddPage(ToolsPage, "Tools", "/tools")
                .AddSections(ToolsMain, isExtendable: true)
                .AddSection(
                    ToolsOverview,
                    "Overview",
                    CoreShellContentReference.Create("tools.overview"),
                    10);
            module
                .AddPage(SettingsPage, "Settings", "/settings/{section?}", isExtendable: true)
                .AddSections(
                    SettingsMain,
                    isExtendable: true,
                    addressMode: CoreShellSectionAddressMode.Child)
                .AddSection(
                    SettingsUsers,
                    "Users",
                    CoreShellContentReference.Create("settings.users"),
                    10);
            module
                .AddMenu(MainMenu, "Main")
                .AddGroup(WorkspaceGroup, "Workspace", 10)
                .AddItem(CoreShellMenuItemId.Create(WorkspaceGroup, "tools"), "Tools", 10)
                .Target(ToolsPage);
        });

    private sealed class DynamicPageResolver(string route) : ICoreShellPageResolver
    {
        public Task<CoreShellResolvedPage?> ResolvePageAsync(
            CoreShellPageResolutionContext context,
            CancellationToken cancellationToken = default)
        {
            CoreShellResolvedPage? resolved = string.Equals(context.Route, route, StringComparison.OrdinalIgnoreCase)
                ? new CoreShellResolvedPage(
                    new CoreShellPageContribution(
                        CoreShellPageId.Create("dynamic"),
                        "Dynamic",
                        route,
                        RoutingMode: CoreShellPageRoutingMode.CoreShellRouted),
                    [],
                    [])
                : null;

            return Task.FromResult(resolved);
        }
    }
}
