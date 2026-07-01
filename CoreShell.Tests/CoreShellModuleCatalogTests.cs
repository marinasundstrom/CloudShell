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

    [Fact]
    public async Task Services_MergeModulesWithoutExposingCompositionPrimitives()
    {
        var catalog = new CoreShellModuleCatalog([CreateShellModule(), CreateExtensionModule()]);

        var pages = await catalog.GetPagesAsync();
        var menu = await catalog.GetMenuAsync(MainMenu);
        var outlets = await catalog.GetSectionOutletsAsync(ToolsPage);
        var sections = await catalog.GetSectionsAsync(ToolsMain);

        Assert.Equal([HomePage, ToolsPage], pages.Select(page => page.Id));
        Assert.NotNull(menu);
        var group = Assert.Single(menu.Groups);
        Assert.Equal(WorkspaceGroup, group.Id);
        Assert.Equal(["Home", "Tools"], group.Items.Select(item => item.Title));
        Assert.Single(outlets);
        Assert.Single(sections);
        Assert.Equal(ToolsOverview, sections[0].Id);
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
                .AddMenu(MainMenu, "Main")
                .AddGroup(WorkspaceGroup, "Workspace", 10)
                .AddItem(CoreShellMenuItemId.Create(WorkspaceGroup, "tools"), "Tools", 10)
                .Target(ToolsPage);
        });
}
