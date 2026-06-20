using System.Text.Json;
using CloudShell.UI.Composition;

namespace CloudShell.UI.Composition.Tests;

public sealed class CompositionDescriptorTests
{
    private static readonly CompositionModuleId ModuleId = CompositionModuleId.Create("workspace");
    private static readonly MenuId MainMenu = MenuId.Create("main");
    private static readonly MenuSectionId WorkspaceMenuSection = MenuSectionId.Create(MainMenu, "workspace");
    private static readonly MenuItemId WorkspaceMenuItem = MenuItemId.Create(MainMenu, "workspace");
    private static readonly MenuItemId DetailsMenuItem = MenuItemId.Create(WorkspaceMenuSection, "details");
    private static readonly PageId WorkspacePage = PageId.Create("workspace");
    private static readonly SectionOutletId MainOutlet = SectionOutletId.Create(WorkspacePage, "main");
    private static readonly SectionId OverviewSection = SectionId.Create(MainOutlet, "overview");
    private static readonly SectionId DetailsSection = SectionId.Create(MainOutlet, "details");

    [Fact]
    public void ToDescriptor_MapsModuleArtifacts()
    {
        var module = CreateModule();

        var descriptor = module.ToDescriptor();

        Assert.Equal(ModuleId, descriptor.Id);
        var page = Assert.Single(descriptor.Pages);
        Assert.Equal(WorkspacePage, page.Id);
        Assert.Equal("Workspace", page.Title);
        Assert.Equal("/workspace", page.Route);

        var menu = Assert.Single(descriptor.Menus);
        Assert.Equal(MainMenu, menu.Id);
        Assert.Single(menu.Items);
        Assert.Single(menu.Sections);

        var section = descriptor.Sections.Single(item => item.Id == DetailsSection);
        Assert.Equal(WorkspacePage, section.PageId);
        Assert.Equal(MainOutlet, section.OutletId);
        Assert.Equal("Details", section.Title);
        Assert.Contains(nameof(DetailsSectionComponent), section.ComponentTypeName);
    }

    [Fact]
    public void Descriptor_CanRoundTripThroughJson()
    {
        var descriptor = CreateModule().ToDescriptor();

        var json = JsonSerializer.Serialize(descriptor);
        var roundTrip = JsonSerializer.Deserialize<CompositionModuleDescriptor>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(descriptor.Id, roundTrip.Id);
        Assert.Equal(descriptor.Pages[0].Id, roundTrip.Pages[0].Id);
        Assert.Equal(descriptor.Menus[0].Sections[0].Items[0].Target, roundTrip.Menus[0].Sections[0].Items[0].Target);
        Assert.Equal(descriptor.Sections[0].ComponentTypeName, roundTrip.Sections[0].ComponentTypeName);
    }

    private static CompositionModule CreateModule() =>
        CompositionModule.Create(ModuleId, module =>
        {
            var menu = module.AddMenu(MainMenu, "Main");
            menu.AddItem(WorkspaceMenuItem, "Workspace", 10).Target(WorkspacePage);
            menu
                .AddSection(WorkspaceMenuSection, "Workspace", 20)
                .AddItem(DetailsMenuItem, "Details", 10)
                .Target(DetailsSection);

            module
                .AddPage(WorkspacePage, "Workspace", "/workspace")
                .AddSections(MainOutlet)
                .AddSection<OverviewSectionComponent>(OverviewSection, "Overview", 10)
                .AddSection<DetailsSectionComponent>(DetailsSection, "Details", 20);
        });

    private sealed class OverviewSectionComponent;

    private sealed class DetailsSectionComponent;
}
