using System.Text.Json;
using CloudShell.UI.Composition;

namespace CloudShell.UI.Composition.Tests;

public sealed class CompositionDescriptorTests
{
    private static readonly CompositionModuleId ModuleId = CompositionModuleId.Create("workspace");
    private static readonly MenuId MainMenu = MenuId.Create("main");
    private static readonly MenuGroupId WorkspaceMenuGroup = MenuGroupId.Create(MainMenu, "workspace");
    private static readonly MenuItemId WorkspaceMenuItem = MenuItemId.Create(MainMenu, "workspace");
    private static readonly MenuItemId DetailsMenuItem = MenuItemId.Create(WorkspaceMenuGroup, "details");
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
        Assert.True(page.IsExtendable);
        Assert.Equal(["workspace.read"], page.Authorization?.AnyPermissions);

        var menu = Assert.Single(descriptor.Menus);
        Assert.Equal(MainMenu, menu.Id);
        Assert.Equal(["menu.read"], menu.Authorization?.AnyPermissions);
        var item = Assert.Single(menu.Items);
        Assert.Equal(["workspace.navigate"], item.Authorization?.AnyPermissions);
        var group = Assert.Single(menu.Groups);
        Assert.Equal(WorkspaceMenuGroup, group.Id);
        Assert.Equal(["workspace.group.read"], group.Authorization?.AnyPermissions);

        var outlet = Assert.Single(descriptor.SectionOutlets);
        Assert.Equal(MainOutlet, outlet.Id);
        Assert.Equal(WorkspacePage, outlet.PageId);
        Assert.False(outlet.IsExtendable);
        Assert.Equal(CompositionSectionAddressMode.Child, outlet.AddressMode);
        Assert.Equal("section", outlet.SelectionKey);
        Assert.Equal(["workspace.sections.read"], outlet.Authorization?.AnyPermissions);

        var section = descriptor.Sections.Single(item => item.Id == DetailsSection);
        Assert.Equal(WorkspacePage, section.PageId);
        Assert.Equal(MainOutlet, section.OutletId);
        Assert.Equal("Details", section.Title);
        Assert.Contains(nameof(DetailsSectionComponent), section.ComponentTypeName);
        Assert.Equal(["workspace.details.read"], section.Authorization?.AnyPermissions);
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
        Assert.Equal(descriptor.Pages[0].IsExtendable, roundTrip.Pages[0].IsExtendable);
        Assert.Equal(descriptor.Pages[0].Authorization?.AnyPermissions, roundTrip.Pages[0].Authorization?.AnyPermissions);
        Assert.Equal(descriptor.Menus[0].Groups[0].Items[0].Target, roundTrip.Menus[0].Groups[0].Items[0].Target);
        Assert.Equal(
            descriptor.Menus[0].Groups[0].Items[0].Authorization?.AnyPermissions,
            roundTrip.Menus[0].Groups[0].Items[0].Authorization?.AnyPermissions);
        Assert.Equal(descriptor.SectionOutlets[0].IsExtendable, roundTrip.SectionOutlets[0].IsExtendable);
        Assert.Equal(descriptor.SectionOutlets[0].AddressMode, roundTrip.SectionOutlets[0].AddressMode);
        Assert.Equal(descriptor.SectionOutlets[0].SelectionKey, roundTrip.SectionOutlets[0].SelectionKey);
        Assert.Equal(
            descriptor.SectionOutlets[0].Authorization?.AnyPermissions,
            roundTrip.SectionOutlets[0].Authorization?.AnyPermissions);
        Assert.Equal(descriptor.Sections[0].ComponentTypeName, roundTrip.Sections[0].ComponentTypeName);
        Assert.Equal(descriptor.Sections[0].Authorization?.AnyPermissions, roundTrip.Sections[0].Authorization?.AnyPermissions);
    }

    [Fact]
    public void FromDescriptor_RehydratesModuleThroughComponentTypeResolver()
    {
        var descriptor = CreateModule().ToDescriptor();
        var resolver = new TestComponentTypeResolver([
            typeof(OverviewSectionComponent),
            typeof(DetailsSectionComponent)]);

        var module = CompositionModule.FromDescriptor(descriptor, resolver);
        var registry = CompositionRegistry.FromModules(module);

        Assert.Equal(ModuleId, module.Id);
        Assert.True(registry.GetPage(WorkspacePage)?.IsExtendable);
        Assert.Equal(["workspace.read"], registry.GetPage(WorkspacePage)?.Authorization.AnyPermissions);
        var details = registry.GetSections(WorkspacePage, MainOutlet)
            .Single(section => section.Id == DetailsSection);
        Assert.Equal(typeof(DetailsSectionComponent), details.ComponentType);
        Assert.Equal(["workspace.details.read"], details.Authorization.AnyPermissions);
    }

    [Fact]
    public void FromDescriptor_RequiresComponentTypeResolverToRecognizeSectionComponents()
    {
        var descriptor = CreateModule().ToDescriptor();
        var resolver = new TestComponentTypeResolver([typeof(OverviewSectionComponent)]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionModule.FromDescriptor(descriptor, resolver));

        Assert.Contains(nameof(DetailsSectionComponent), exception.Message);
    }

    private static CompositionModule CreateModule() =>
        CompositionModule.Create(ModuleId, module =>
        {
            var menu = module.AddMenu(MainMenu, "Main");
            menu.RequiresPermissions("menu.read");
            menu
                .AddItem(WorkspaceMenuItem, "Workspace", 10)
                .RequiresPermissions("workspace.navigate")
                .Target(WorkspacePage);
            menu
                .AddGroup(WorkspaceMenuGroup, "Workspace", 20)
                .RequiresPermissions("workspace.group.read")
                .AddItem(DetailsMenuItem, "Details", 10)
                .Target(DetailsSection);

            module
                .AddPage(WorkspacePage, "Workspace", "/workspace", isExtendable: true)
                .RequiresPermissions("workspace.read")
                .AddSections(
                    MainOutlet,
                    authorization: CompositionAuthorizationRequirements.FromAnyPermissions(["workspace.sections.read"]),
                    addressMode: CompositionSectionAddressMode.Child)
                .AddSection<OverviewSectionComponent>(OverviewSection, "Overview", 10)
                .AddSection(
                    DetailsSection,
                    "Details",
                    typeof(DetailsSectionComponent),
                    20,
                    CompositionAuthorizationRequirements.FromAnyPermissions(["workspace.details.read"]));
        });

    private sealed class OverviewSectionComponent;

    private sealed class DetailsSectionComponent;

    private sealed class TestComponentTypeResolver(IEnumerable<Type> componentTypes) : ICompositionComponentTypeResolver
    {
        private readonly IReadOnlyDictionary<string, Type> _componentTypes = componentTypes.ToDictionary(
            componentType => componentType.AssemblyQualifiedName ?? componentType.FullName ?? componentType.Name,
            componentType => componentType);

        public Type ResolveComponentType(string componentTypeName)
        {
            if (_componentTypes.TryGetValue(componentTypeName, out var componentType))
            {
                return componentType;
            }

            throw new InvalidOperationException($"Composition component type '{componentTypeName}' is not registered.");
        }
    }
}
