using CoreShell;
using CoreShell.Composition;

namespace CoreShell.Tests;

public sealed class CoreShellModuleTests
{
    private static readonly CoreShellModuleId Module = CoreShellModuleId.Create("cloudshell.core");
    private static readonly CoreShellMenuId MainMenu = CoreShellMenuId.Create("cloudshell.main");
    private static readonly CoreShellMenuGroupId WorkspaceGroup = CoreShellMenuGroupId.Create(MainMenu, "workspace");
    private static readonly CoreShellMenuItemId OverviewItem = CoreShellMenuItemId.Create(WorkspaceGroup, "overview");
    private static readonly CoreShellPageId OverviewPage = CoreShellPageId.Create("cloudshell.overview");
    private static readonly CoreShellPageId SettingsPage = CoreShellPageId.Create("cloudshell.settings");
    private static readonly CoreShellSectionOutletId SettingsMain = CoreShellSectionOutletId.Create(SettingsPage, "main");
    private static readonly CoreShellSectionId SettingsOverview = CoreShellSectionId.Create(SettingsMain, "overview");
    private static readonly CoreShellContentReference SettingsPageContent =
        CoreShellContentReference.Create("cloudshell.settings.page");
    private static readonly CoreShellContentReference SettingsOverviewContent =
        CoreShellContentReference.Create("cloudshell.settings.overview");
    private static readonly CoreShellLayoutReference SettingsLayout =
        CoreShellLayoutReference.Create("cloudshell.layout.settings");
    private static readonly CoreShellLayoutReference SettingsSectionLayout =
        CoreShellLayoutReference.Create("cloudshell.layout.settings.section");

    [Fact]
    public void Create_BuildsFrameworkNeutralShellModule()
    {
        var module = CreateModule();

        Assert.Equal(Module, module.Id);
        Assert.Equal(2, module.Pages.Count);
        Assert.Contains(module.Pages, page => page.Id == OverviewPage && page.Route == "/");
        var settingsPage = Assert.Single(module.Pages, page => page.Id == SettingsPage);
        Assert.True(settingsPage.IsExtendable);
        Assert.Equal(SettingsPageContent, settingsPage.Content);
        Assert.Equal(SettingsLayout, settingsPage.Layout);
        Assert.Equal(CoreShellPageRoutingMode.CoreShellRouted, settingsPage.RoutingMode);

        var outlet = Assert.Single(module.SectionOutlets);
        Assert.Equal(SettingsMain, outlet.Id);
        Assert.Equal(CoreShellSectionAddressMode.Child, outlet.AddressMode);
        Assert.Equal(SettingsSectionLayout, outlet.Layout);

        var section = Assert.Single(module.Sections);
        Assert.Equal(SettingsOverviewContent, section.Content);
        Assert.Equal(SettingsSectionLayout, section.Layout);
        Assert.Equal(["shell.configure"], section.Authorization.AnyPermissions);

        var menu = Assert.Single(module.Menus);
        var group = Assert.Single(menu.Groups);
        var item = Assert.Single(group.Items);
        Assert.Equal(OverviewItem, item.Id);
        Assert.Equal(CoreShellTarget.ForPage(OverviewPage), item.Target);
    }

    [Fact]
    public void CompositionProjector_MapsCoreShellModuleToCompositionModule()
    {
        var resolver = new TestContentResolver(new Dictionary<CoreShellContentReference, Type>
        {
            [SettingsOverviewContent] = typeof(SettingsOverviewComponent)
        });

        var composition = new CoreShellCompositionProjector(resolver).CreateModule(CreateModule());
        var registry = CompositionRegistry.FromModules(composition);

        Assert.Equal(CompositionModuleId.Create(Module.Value), composition.Id);
        Assert.NotNull(registry.GetPage(PageId.Create(OverviewPage.Value)));
        Assert.True(registry.GetPage(PageId.Create(SettingsPage.Value))?.IsExtendable);

        var sections = registry.GetSections(
            PageId.Create(SettingsPage.Value),
            new SectionOutletId($"section-outlet.{SettingsMain.Value}"));
        var section = Assert.Single(sections);
        Assert.Equal(typeof(SettingsOverviewComponent), section.ComponentType);
        Assert.Equal(["shell.configure"], section.Authorization.AnyPermissions);

        var menu = registry.GetMenu(MenuId.Create(MainMenu.Value));
        Assert.NotNull(menu);
        Assert.Equal("Main", menu.Title);
        Assert.Single(menu.Groups);
    }

    private static CoreShellModule CreateModule() =>
        CoreShellModule.Create(Module, module =>
        {
            module.AddPage(OverviewPage, "Overview", "/");

            module
                .AddPage(
                    SettingsPage,
                    "Settings",
                    "/settings/{section?}",
                    isExtendable: true,
                    content: SettingsPageContent,
                    layout: SettingsLayout,
                    routingMode: CoreShellPageRoutingMode.CoreShellRouted)
                .AddSections(
                    SettingsMain,
                    isExtendable: true,
                    addressMode: CoreShellSectionAddressMode.Child,
                    layout: SettingsSectionLayout)
                .AddSection(
                    SettingsOverview,
                    "Overview",
                    SettingsOverviewContent,
                    10,
                    CoreShellAuthorizationRequirements.FromAnyPermissions(["shell.configure"]),
                    layout: SettingsSectionLayout);

            var menu = module.AddMenu(MainMenu, "Main");
            menu
                .AddGroup(WorkspaceGroup, "Workspace", 10)
                .AddItem(OverviewItem, "Overview", 0)
                .Target(OverviewPage);
        });

    private sealed class SettingsOverviewComponent;

    private sealed class TestContentResolver(
        IReadOnlyDictionary<CoreShellContentReference, Type> contentTypes) : ICoreShellContentResolver
    {
        public Type ResolveContentType(CoreShellContentReference content) =>
            contentTypes.TryGetValue(content, out var componentType)
                ? componentType
                : throw new InvalidOperationException($"CoreShell content '{content}' is not registered.");
    }
}
