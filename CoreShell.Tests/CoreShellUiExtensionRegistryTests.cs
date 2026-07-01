using CoreShell;

namespace CoreShell.Tests;

public sealed class CoreShellUiExtensionRegistryTests
{
    [Fact]
    public void GetModules_BuildsModulesFromUiExtensions()
    {
        var extension = new TestUiExtension();
        var registry = new CoreShellUiExtensionRegistry([extension]);

        var module = Assert.Single(registry.GetModules());

        Assert.Equal(TestUiExtension.ModuleId, module.Id);
        var page = Assert.Single(module.Pages);
        Assert.Equal(TestUiExtension.PageId, page.Id);
        var menu = Assert.Single(module.Menus);
        Assert.Equal(TestUiExtension.MenuId, menu.Id);
    }

    private sealed class TestUiExtension : ICoreShellUiExtension
    {
        public static readonly CoreShellModuleId ModuleId = CoreShellModuleId.Create("sample");
        public static readonly CoreShellPageId PageId = CoreShellPageId.Create("sample.home");
        public static readonly CoreShellMenuId MenuId = CoreShellMenuId.Create("main");
        private static readonly CoreShellMenuGroupId WorkspaceGroup = CoreShellMenuGroupId.Create(MenuId, "workspace");

        public CoreShellUiExtensionManifest Manifest { get; } = new(
            ModuleId,
            "Sample");

        public void Configure(CoreShellModuleBuilder builder)
        {
            builder.AddPage(PageId, "Sample", "/sample");
            builder
                .AddMenu(MenuId, "Main")
                .AddGroup(WorkspaceGroup, "Workspace", 10)
                .AddItem(CoreShellMenuItemId.Create(WorkspaceGroup, "sample"), "Sample", 10)
                .Target(PageId);
        }
    }
}
