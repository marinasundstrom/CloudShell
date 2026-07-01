using CoreShell.Blazor;
using Microsoft.AspNetCore.Components;

namespace CoreShell.Tests;

public sealed class CoreShellBlazorPageProjectionTests
{
    private static readonly CoreShellModuleId Module = CoreShellModuleId.Create("shell");
    private static readonly CoreShellPageId SettingsPage = CoreShellPageId.Create("settings");
    private static readonly CoreShellSectionOutletId SettingsMain = CoreShellSectionOutletId.Create(SettingsPage, "main");
    private static readonly CoreShellSectionId UsersSection = CoreShellSectionId.Create(SettingsMain, "users");

    [Fact]
    public async Task ResolvePageAsync_ProjectsAddressablePageAndSectionsToBlazorComponents()
    {
        var catalog = new CoreShellModuleCatalog([CreateModule()]);
        var projection = new CoreShellBlazorPageProjectionService(
            new CoreShellPageResolutionService([catalog]),
            new BlazorCoreShellContentResolver(),
            new BlazorCoreShellLayoutResolver());

        var resolved = await projection.ResolvePageAsync(new CoreShellPageResolutionContext("/settings/users"));

        Assert.NotNull(resolved);
        Assert.Equal(SettingsPage, resolved.Page.Id);
        Assert.Equal("/settings/{section?}", resolved.Page.Route);
        Assert.Equal(CoreShellPageRoutingMode.CoreShellRouted, resolved.Page.RoutingMode);
        Assert.Equal(typeof(SettingsPageComponent), resolved.ContentType);
        Assert.Equal(typeof(SettingsLayoutComponent), resolved.LayoutType);

        var outlet = Assert.Single(resolved.SectionOutlets);
        Assert.Equal(SettingsMain, outlet.Outlet.Id);
        Assert.Equal(CoreShellSectionAddressMode.Child, outlet.Outlet.AddressMode);
        Assert.Equal("section", outlet.Outlet.SelectionKey);
        Assert.Equal(typeof(SettingsOutletLayoutComponent), outlet.LayoutType);

        var section = Assert.Single(resolved.Sections);
        Assert.Equal(UsersSection, section.Section.Id);
        Assert.Equal(typeof(UsersSectionComponent), section.ContentType);
        Assert.Equal(typeof(SettingsSectionLayoutComponent), section.LayoutType);
        Assert.Equal([section], resolved.GetSections(SettingsMain));
    }

    [Fact]
    public async Task ResolvePageAsync_ReturnsNullWhenCoreShellRouteIsNotKnown()
    {
        var catalog = new CoreShellModuleCatalog([CreateModule()]);
        var projection = new CoreShellBlazorPageProjectionService(
            new CoreShellPageResolutionService([catalog]),
            new BlazorCoreShellContentResolver());

        var resolved = await projection.ResolvePageAsync(new CoreShellPageResolutionContext("/missing"));

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveSectionOutletAsync_ProjectsOutletByCoreShellIds()
    {
        var catalog = new CoreShellModuleCatalog([CreateModule()]);
        var projection = new CoreShellBlazorSectionOutletProjectionService(
            catalog,
            catalog,
            new BlazorCoreShellContentResolver(),
            new BlazorCoreShellLayoutResolver());

        var resolved = await projection.ResolveSectionOutletAsync(SettingsPage, SettingsMain);

        Assert.NotNull(resolved);
        Assert.Equal(SettingsPage, resolved.Page.Id);
        Assert.Equal(SettingsMain, resolved.Outlet.Outlet.Id);
        Assert.Equal(CoreShellSectionAddressMode.Child, resolved.Outlet.Outlet.AddressMode);

        var section = Assert.Single(resolved.Sections);
        Assert.Equal(UsersSection, section.Section.Id);
        Assert.Equal(typeof(UsersSectionComponent), section.ContentType);
        Assert.Equal(typeof(SettingsSectionLayoutComponent), section.LayoutType);
    }

    private static CoreShellModule CreateModule() =>
        CoreShellModule.Create(Module, module =>
        {
            module
                .AddPage(
                    SettingsPage,
                    "Settings",
                    "/settings/{section?}",
                    isExtendable: true,
                    content: CoreShellBlazorContent.For<SettingsPageComponent>(),
                    layout: CoreShellBlazorLayout.For<SettingsLayoutComponent>(),
                    routingMode: CoreShellPageRoutingMode.CoreShellRouted)
                .AddSections(
                    SettingsMain,
                    isExtendable: true,
                    addressMode: CoreShellSectionAddressMode.Child,
                    layout: CoreShellBlazorLayout.For<SettingsOutletLayoutComponent>())
                .AddSection(
                    UsersSection,
                    "Users",
                    CoreShellBlazorContent.For<UsersSectionComponent>(),
                    10,
                    layout: CoreShellBlazorLayout.For<SettingsSectionLayoutComponent>());
        });

    private sealed class SettingsPageComponent : ComponentBase;

    private sealed class SettingsLayoutComponent : ComponentBase;

    private sealed class SettingsOutletLayoutComponent : ComponentBase;

    private sealed class SettingsSectionLayoutComponent : ComponentBase;

    private sealed class UsersSectionComponent : ComponentBase;
}
