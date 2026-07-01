using CoreShell.Composition;

namespace CoreShell.Composition.Tests;

public sealed class CompositionRegistryTests
{
    private static readonly MenuId MainMenu = new("menu.main");
    private static readonly MenuGroupId WorkspaceMenuGroup = new("menu-group.main.workspace");
    private static readonly MenuItemId WorkspaceMenuItem = new("menu-item.main.workspace");
    private static readonly MenuItemId SectionMenuItem = new("menu-item.main.workspace.section");
    private static readonly MenuItemId ChildMenuItem = new("menu-item.main.workspace.child");
    private static readonly MenuItemId ReportsMenuItem = new("menu-item.main.workspace.reports");
    private static readonly PageId WorkspacePage = new("page.workspace");
    private static readonly PageId ReportsPage = new("page.reports");
    private static readonly PageId ResourceDetailsPage = new("page.resources.details");
    private static readonly SectionOutletId MainOutlet = new("section-outlet.workspace.main");
    private static readonly SectionOutletId ExtensionOutlet = new("section-outlet.workspace.extension");
    private static readonly SectionOutletId ReportsOutlet = new("section-outlet.reports.main");
    private static readonly SectionId OverviewSection = new("section.workspace.main.overview");
    private static readonly SectionId DetailsSection = new("section.workspace.main.details");
    private static readonly SectionId ReportsSummarySection = new("section.reports.main.summary");
    private static readonly SectionId ExtensionOwnedSection = new("section.workspace.extension.summary");
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
    public void GetPageByRoute_PreservesOptionalRouteTemplateParameters()
    {
        var registry = CompositionRegistry.Create(composition =>
        {
            composition.AddPage(ResourceDetailsPage, "Resource details", "/resources/{resourceId}/{view?}");
        });

        var page = registry.GetPageByRoute("/resources/{resourceId}/{view?}?traceId=123");

        Assert.NotNull(page);
        Assert.Equal(ResourceDetailsPage, page.Id);
    }

    [Fact]
    public void GetPageByRoute_MatchesMaterializedRouteTemplateParameters()
    {
        var registry = CompositionRegistry.Create(composition =>
        {
            composition.AddPage(ResourceDetailsPage, "Resource details", "/resources/{resourceId}/{view?}");
        });

        var page = registry.GetPageByRoute("/resources/application%3Aapi/logs?traceId=123#activity");

        Assert.NotNull(page);
        Assert.Equal(ResourceDetailsPage, page.Id);
    }

    [Fact]
    public void GetPageByRoute_MatchesMaterializedRouteTemplatesWithOptionalSegmentsOmitted()
    {
        var registry = CompositionRegistry.Create(composition =>
        {
            composition.AddPage(ResourceDetailsPage, "Resource details", "/resources/{resourceId}/{view?}");
        });

        var page = registry.GetPageByRoute("/resources/application%3Aapi");

        Assert.NotNull(page);
        Assert.Equal(ResourceDetailsPage, page.Id);
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
    public void ResolveHref_MaterializesPageRouteTemplateParameters()
    {
        var registry = CompositionRegistry.Create(composition =>
        {
            composition.AddPage(ResourceDetailsPage, "Resource details", "/resources/{resourceId}/{view?}");
        });

        var href = registry.ResolveHref(
            ResourceDetailsPage,
            new Dictionary<string, object?>
            {
                ["resourceId"] = "application:orders api",
                ["view"] = "overview",
                ["traceId"] = "4bf92f"
            });

        Assert.Equal("/resources/application%3Aorders%20api/overview?traceId=4bf92f", href);
    }

    [Fact]
    public void ResolveHref_OmitsMissingOptionalPageRouteTemplateSegments()
    {
        var registry = CompositionRegistry.Create(composition =>
        {
            composition.AddPage(ResourceDetailsPage, "Resource details", "/resources/{resourceId}/{view?}");
        });

        var href = registry.ResolveHref(
            ResourceDetailsPage,
            new Dictionary<string, object?>
            {
                ["resourceId"] = "application:orders-api"
            });

        Assert.Equal("/resources/application%3Aorders-api", href);
    }

    [Fact]
    public void ResolveHref_OmitsOptionalPageRouteTemplateSegmentsWithoutRouteParameters()
    {
        var settingsPage = new PageId("page.settings");
        var registry = CompositionRegistry.Create(composition =>
        {
            composition.AddPage(settingsPage, "Settings", "/settings/{section?}");
        });

        var href = registry.ResolveHref(settingsPage);

        Assert.Equal("/settings", href);
    }

    [Fact]
    public void ResolveHref_MaterializesConstrainedPageRouteTemplateParameters()
    {
        var constrainedPage = new PageId("page.resources.by-number");
        var registry = CompositionRegistry.Create(composition =>
        {
            composition.AddPage(constrainedPage, "Resource details", "/resources/{resourceId:int}/details");
        });

        var href = registry.ResolveHref(
            constrainedPage,
            new Dictionary<string, object?>
            {
                ["resourceId"] = 42
            });

        Assert.Equal("/resources/42/details", href);
    }

    [Fact]
    public void ResolveHref_ReturnsHashForMissingPageRouteTemplateParameters()
    {
        var registry = CompositionRegistry.Create(composition =>
        {
            composition.AddPage(ResourceDetailsPage, "Resource details", "/resources/{resourceId}/details");
        });

        var href = registry.ResolveHref(
            ResourceDetailsPage,
            new Dictionary<string, object?>
            {
                ["tab"] = "general:overview"
            });

        Assert.Equal("#", href);
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
    public void ResolveHref_ResolvesChildAddressedSectionTargetsThroughOwningPageRoute()
    {
        var settingsPage = new PageId("page.settings");
        var settingsOutlet = SectionOutletId.Create(settingsPage, "main");
        var advancedSection = SectionId.Create(settingsOutlet, "advanced");
        var registry = CompositionRegistry.Create(composition =>
        {
            composition
                .AddPage(settingsPage, "Settings", "/settings/{section?}")
                .AddSections(settingsOutlet)
                .UseChildAddresses()
                .AddSection<OverviewSectionComponent>(advancedSection, "Advanced", 10);
        });

        var href = registry.ResolveHref(
            advancedSection,
            new Dictionary<string, object?>
            {
                ["culture"] = "en"
            });

        Assert.Equal("/settings/advanced?culture=en", href);
    }

    [Fact]
    public void GetSectionAddressValue_ReturnsSectionSelectionValue()
    {
        var settingsPage = new PageId("page.settings");
        var settingsOutlet = SectionOutletId.Create(settingsPage, "main");
        var advancedSection = SectionId.Create(settingsOutlet, "advanced");
        var registry = CompositionRegistry.Create(composition =>
        {
            composition
                .AddPage(settingsPage, "Settings", "/settings/{section?}")
                .AddSections(settingsOutlet)
                .UseChildAddresses()
                .AddSection<OverviewSectionComponent>(advancedSection, "Advanced", 10);
        });

        var addressValue = registry.GetSectionAddressValue(advancedSection);

        Assert.Equal("advanced", addressValue);
    }

    [Fact]
    public void FromModules_RejectsDuplicateChildAddressedSections()
    {
        var settingsPage = new PageId("page.settings");
        var settingsOutlet = SectionOutletId.Create(settingsPage, "main");
        var firstSection = new SectionId("custom.settings.first.details");
        var secondSection = new SectionId("custom.settings.second.details");
        var module = CompositionModule.Create(CompositionModuleId.Host, composition =>
        {
            composition
                .AddPage(settingsPage, "Settings", "/settings/{section?}")
                .AddSections(settingsOutlet, addressMode: CompositionSectionAddressMode.Child)
                .AddSection<OverviewSectionComponent>(firstSection, "First", 10)
                .AddSection<DetailsSectionComponent>(secondSection, "Second", 20);
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.FromModules(module));

        Assert.Contains("uses child addresses", exception.Message);
        Assert.Contains("details", exception.Message);
    }

    [Fact]
    public void FromModules_RejectsChildAddressedOutletUnderParentAddressedSection()
    {
        var childOutlet = SectionOutletId.Create(OverviewSection, "children");
        var childSection = SectionId.Create(childOutlet, "child");
        var module = CompositionModule.Create(CompositionModuleId.Host, composition =>
        {
            var page = composition.AddPage(WorkspacePage, "Workspace", "/workspace");
            page
                .AddSections(MainOutlet)
                .AddSection<OverviewSectionComponent>(OverviewSection, "Overview", 10);

            page
                .AddSections(childOutlet)
                .UseChildAddresses()
                .AddSection<DetailsSectionComponent>(childSection, "Child", 10);
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.FromModules(module));

        Assert.Contains("cannot use child addresses", exception.Message);
        Assert.Contains(OverviewSection.Value, exception.Message);
    }

    [Fact]
    public void ResolveHref_ReturnsDirectHrefTargets()
    {
        var registry = CreateRegistry();

        var target = CompositionTarget.ForHref("/workspace/child");
        var href = registry.ResolveHref(target);

        Assert.Equal(CompositionTargetKind.Href, target.Kind);
        Assert.Equal("/workspace/child", href);
    }

    [Fact]
    public void ResolveHref_AppendsRouteParametersBeforeFragments()
    {
        var registry = CreateRegistry();

        var href = registry.ResolveHref(
            CompositionTarget.ForHref("/workspace#summary"),
            new Dictionary<string, object?>
            {
                ["view"] = "summary"
            });

        Assert.Equal("/workspace?view=summary#summary", href);
    }

    [Fact]
    public void ResolveHref_ResolvesMenuItemTargetsThroughTheirRegisteredTargets()
    {
        var registry = CreateRegistry();

        var pageHref = registry.ResolveHref(WorkspaceMenuItem);
        var hrefHref = registry.ResolveHref(ChildMenuItem);

        Assert.Equal("/workspace", pageHref);
        Assert.Equal("/workspace/child", hrefHref);
    }

    [Fact]
    public void GetMenuItemProjection_ReturnsMenuItemContext()
    {
        var registry = CreateRegistry();

        var projection = registry.GetMenuItemProjection(ChildMenuItem);

        Assert.NotNull(projection);
        Assert.Equal(MainMenu, projection.Menu.Id);
        Assert.Equal(WorkspaceMenuGroup, projection.Group?.Id);
        Assert.Equal("Child", projection.Item.Title);
    }

    [Fact]
    public void GetMenuItemProjections_ReturnsMenuScopedItemContexts()
    {
        var registry = CreateRegistry();

        var projections = registry.GetMenuItemProjections(MainMenu);

        Assert.Collection(
            projections,
            projection =>
            {
                Assert.Equal(CompositionModuleId.Host, projection.ModuleId);
                Assert.Null(projection.Group);
                Assert.Equal(WorkspaceMenuItem, projection.Item.Id);
            },
            projection =>
            {
                Assert.Equal(CompositionModuleId.Host, projection.ModuleId);
                Assert.Equal(WorkspaceMenuGroup, projection.Group?.Id);
                Assert.Equal(SectionMenuItem, projection.Item.Id);
            },
            projection =>
            {
                Assert.Equal(CompositionModuleId.Host, projection.ModuleId);
                Assert.Equal(WorkspaceMenuGroup, projection.Group?.Id);
                Assert.Equal(ChildMenuItem, projection.Item.Id);
            });
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
    public void Create_RejectsDuplicateMenuGroupIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.Create(composition =>
            {
                var menu = composition.AddMenu(MainMenu, "Main");
                menu.AddGroup(WorkspaceMenuGroup, "Workspace", 10);
                menu.AddGroup(WorkspaceMenuGroup, "Workspace duplicate", 20);
            }));

        Assert.Contains("Duplicate composition menu group ID", exception.Message);
    }

    [Fact]
    public void Create_RejectsDuplicateMenuItemIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.Create(composition =>
            {
                var menu = composition.AddMenu(MainMenu, "Main");
                menu.AddItem(WorkspaceMenuItem, "Workspace", 10).Target(WorkspacePage);
                menu.AddItem(WorkspaceMenuItem, "Workspace duplicate", 20).Target(WorkspacePage);
            }));

        Assert.Contains("Duplicate composition menu item ID", exception.Message);
    }

    [Fact]
    public void Create_RejectsMenuItemsWithUnknownParents()
    {
        var missingParent = new MenuItemId("menu-item.main.missing");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.Create(composition =>
            {
                composition
                    .AddMenu(MainMenu, "Main")
                    .AddItem(ChildMenuItem, "Child", 10)
                    .WithParent(missingParent)
                    .TargetHref("/workspace/child");
            }));

        Assert.Contains("unknown parent menu item", exception.Message);
        Assert.Contains(missingParent.Value, exception.Message);
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
    public void AddMenu_RegistersRootItemsGroupsAndSubItems()
    {
        var registry = CreateRegistry();

        var menu = registry.GetMenu(MainMenu);

        Assert.NotNull(menu);
        Assert.Equal("Main", menu.Title);
        var item = Assert.Single(menu.Items);
        Assert.Equal(WorkspaceMenuItem, item.Id);
        Assert.Equal(WorkspacePage.Value, item.Target.Value);
        Assert.Equal("resources", item.Attributes[CompositionAttributeNames.Icon]);
        Assert.Equal(new[] { "resource.read" }, item.PermissionsRequiredForNavigation);
        var group = Assert.Single(menu.Groups);
        Assert.Equal(WorkspaceMenuGroup, group.Id);
        Assert.Equal("Workspace sections", group.Title);
        Assert.Collection(
            group.Items,
            groupItem =>
            {
                Assert.Equal(SectionMenuItem, groupItem.Id);
                Assert.Equal(DetailsSection.Value, groupItem.Target.Value);
                Assert.Null(groupItem.ParentId);
            },
            childItem =>
            {
                Assert.Equal(ChildMenuItem, childItem.Id);
                Assert.Equal(SectionMenuItem, childItem.ParentId);
                Assert.Equal("/workspace/child", childItem.Target.Value);
            });
    }

    [Fact]
    public void FromModules_MergesMenuContributionsByMenuAndGroup()
    {
        var host = CreateHostModule();
        var reports = CompositionModule.Create(ReportsModule, module =>
        {
            module
                .GetMenu(MainMenu)
                .AddGroup(WorkspaceMenuGroup, "Workspace sections", 20)
                .AddItem(ReportsMenuItem, "Reports", 30)
                .Target(ReportsPage);
        });

        var registry = CompositionRegistry.FromModules(host, reports);

        var menu = registry.GetMenu(MainMenu);
        Assert.NotNull(menu);
        Assert.Equal(["menu.read"], menu.Authorization.AnyPermissions);
        var group = Assert.Single(menu.Groups);
        Assert.Equal(WorkspaceMenuGroup, group.Id);
        Assert.Equal(["workspace.read"], group.Authorization.AnyPermissions);
        Assert.Collection(
            group.Items,
            item => Assert.Equal(SectionMenuItem, item.Id),
            item => Assert.Equal(ChildMenuItem, item.Id),
            item => Assert.Equal(ReportsMenuItem, item.Id));
        var reportsProjection = registry.GetMenuItemProjection(ReportsMenuItem);
        Assert.NotNull(reportsProjection);
        Assert.Equal(ReportsModule, reportsProjection.ModuleId);
        Assert.Equal(MainMenu, reportsProjection.Menu.Id);
        Assert.Equal(WorkspaceMenuGroup, reportsProjection.Group?.Id);
    }

    [Fact]
    public void Create_MergesMenuContributionsFromSameBuilder()
    {
        var registry = CompositionRegistry.Create(composition =>
        {
            composition
                .AddMenu(MainMenu, "Main")
                .AddItem(WorkspaceMenuItem, "Workspace", 10)
                .Target(WorkspacePage);

            composition
                .GetMenu(MainMenu)
                .AddGroup(WorkspaceMenuGroup, "Workspace sections", 20)
                .AddItem(ReportsMenuItem, "Reports", 30)
                .Target(ReportsPage);
        });

        var menu = registry.GetMenu(MainMenu);

        Assert.NotNull(menu);
        Assert.Equal(WorkspaceMenuItem, Assert.Single(menu.Items).Id);
        Assert.Equal(ReportsMenuItem, Assert.Single(Assert.Single(menu.Groups).Items).Id);
    }

    [Fact]
    public void Create_AssignsDefaultHostModule()
    {
        var registry = CreateRegistry();

        var module = Assert.Single(registry.Modules);

        Assert.Equal(CompositionModuleId.Host, module.Id);
        Assert.Single(module.Pages);
        Assert.Single(module.Menus);
        var outlet = Assert.Single(module.SectionOutlets);
        Assert.Equal(MainOutlet, outlet.Id);
        Assert.True(outlet.IsExtendable);
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

    [Fact]
    public void FromModules_AllowsExternalSectionsForExtendableOutlets()
    {
        var host = CreateHostModule();
        var extensionSection = SectionId.Create(MainOutlet, "extension");
        var extension = CompositionModule.Create(ReportsModule, module =>
        {
            module
                .GetSections(WorkspacePage, MainOutlet)
                .AddSection<ReportsSectionComponent>(extensionSection, "Extension", 30);
        });

        var registry = CompositionRegistry.FromModules(host, extension);

        var section = registry.GetSectionProjections(WorkspacePage, MainOutlet)
            .Single(projection => projection.Section.Id == extensionSection);
        Assert.Equal(ReportsModule, section.ModuleId);
    }

    [Fact]
    public void FromModules_RejectsExternalSectionsForNonExtendableOutlets()
    {
        var host = CompositionModule.Create(CompositionModuleId.Host, module =>
        {
            module
                .AddPage(WorkspacePage, "Workspace", "/workspace")
                .AddSections(MainOutlet)
                .AddSection<OverviewSectionComponent>(OverviewSection, "Overview", 10);
        });
        var extensionSection = SectionId.Create(MainOutlet, "extension");
        var extension = CompositionModule.Create(ReportsModule, module =>
        {
            module
                .GetSections(WorkspacePage, MainOutlet)
                .AddSection<ReportsSectionComponent>(extensionSection, "Extension", 30);
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.FromModules(host, extension));

        Assert.Contains("not extendable", exception.Message);
        Assert.Contains(extensionSection.Value, exception.Message);
    }

    [Fact]
    public void FromModules_RejectsSectionsForUnknownOutlets()
    {
        var extension = CompositionModule.Create(ReportsModule, module =>
        {
            module
                .GetSections(WorkspacePage, MainOutlet)
                .AddSection<ReportsSectionComponent>(OverviewSection, "Extension", 10);
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.FromModules(extension));

        Assert.Contains("unknown section outlet", exception.Message);
    }

    [Fact]
    public void ProjectionQueries_PreserveModuleOwnership()
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

        var page = registry.GetPageProjection(ReportsPage);
        var menu = registry.GetMenuProjection(MainMenu);
        var section = Assert.Single(registry.GetSectionProjections(ReportsPage, ReportsOutlet));

        Assert.NotNull(page);
        Assert.Equal(ReportsModule, page.ModuleId);
        Assert.NotNull(menu);
        Assert.Equal(CompositionModuleId.Host, menu.ModuleId);
        Assert.Equal(ReportsModule, section.ModuleId);
        Assert.Equal(ReportsSummarySection, section.Section.Id);
    }

    [Fact]
    public void Extend_AddsSectionToExtendableOutletOwnedByAnotherModule()
    {
        var host = CreateHostModule();
        var extension = CompositionModule.Create(ReportsModule, module =>
        {
            module
                .Extend(new CompositionSectionOutletExtensionPoint(WorkspacePage, MainOutlet))
                .AddSection<ReportsSectionComponent>(
                    ReportsSummarySection,
                    "Extension",
                    30);
        });

        var registry = CompositionRegistry.FromModules(host, extension);

        var section = registry
            .GetSectionProjections(WorkspacePage, MainOutlet)
            .Single(projection => projection.Section.Id == ReportsSummarySection);

        Assert.Equal(ReportsModule, section.ModuleId);
        Assert.Equal("Extension", section.Section.Title);
    }

    [Fact]
    public void ExtendPage_AddsExtensionOwnedOutletToHostPage()
    {
        var host = CreateHostModule();
        var extension = CompositionModule.Create(ReportsModule, module =>
        {
            module
                .Extend(WorkspacePage)
                .AddSections(ExtensionOutlet)
                .AddSection<ReportsSectionComponent>(
                    ExtensionOwnedSection,
                    "Extension outlet",
                    10);
        });

        var registry = CompositionRegistry.FromModules(host, extension);

        var outlet = registry.GetSectionOutletProjection(ExtensionOutlet);
        var section = Assert.Single(registry.GetSectionProjections(WorkspacePage, ExtensionOutlet));

        Assert.NotNull(outlet);
        Assert.Equal(ReportsModule, outlet.ModuleId);
        Assert.Equal(ReportsModule, section.ModuleId);
        Assert.Equal(ExtensionOwnedSection, section.Section.Id);
    }

    [Fact]
    public void ExtendPage_ThrowsWhenPageIsNotExtendable()
    {
        var host = CompositionModule.Create(CompositionModuleId.Host, module =>
        {
            module.AddPage(WorkspacePage, "Workspace", "/workspace");
        });
        var extension = CompositionModule.Create(ReportsModule, module =>
        {
            module
                .Extend(WorkspacePage)
                .AddSections(ExtensionOutlet)
                .AddSection<ReportsSectionComponent>(
                    ExtensionOwnedSection,
                    "Extension outlet",
                    10);
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CompositionRegistry.FromModules(host, extension));

        Assert.Contains("is not extendable", exception.Message);
        Assert.Contains(WorkspacePage.Value, exception.Message);
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
            .WithAttribute(CompositionAttributeNames.Icon, "resources")
            .RequiresPermissions("resource.read")
            .Target(WorkspacePage);
        var group = menu.AddGroup(WorkspaceMenuGroup, "Workspace sections", 20);
        group
            .AddItem(SectionMenuItem, "Details", 10)
            .Target(DetailsSection);
        group
            .AddItem(ChildMenuItem, "Child", 20)
            .WithParent(SectionMenuItem)
            .TargetHref("/workspace/child");

        var page = composition.AddPage(WorkspacePage, "Workspace", "/workspace", isExtendable: true);
        page
            .AddSections(MainOutlet, isExtendable: true)
            .AddSection<OverviewSectionComponent>(OverviewSection, "Overview", 10)
            .AddSection<DetailsSectionComponent>(DetailsSection, "Details", 10);
    }

    private static void ConfigureHostModule(CompositionModuleBuilder composition)
    {
        var menu = composition.AddMenu(MainMenu, "Main");
        menu.RequiresPermissions("menu.read");
        menu
            .AddItem(WorkspaceMenuItem, "Workspace", 10)
            .WithAttribute(CompositionAttributeNames.Icon, "resources")
            .RequiresPermissions("resource.read")
            .Target(WorkspacePage);
        var group = menu
            .AddGroup(WorkspaceMenuGroup, "Workspace sections", 20)
            .RequiresPermissions("workspace.read");
        group
            .AddItem(SectionMenuItem, "Details", 10)
            .Target(DetailsSection);
        group
            .AddItem(ChildMenuItem, "Child", 20)
            .WithParent(SectionMenuItem)
            .TargetHref("/workspace/child");

        var page = composition.AddPage(WorkspacePage, "Workspace", "/workspace", isExtendable: true);
        page
            .AddSections(MainOutlet, isExtendable: true)
            .AddSection<OverviewSectionComponent>(OverviewSection, "Overview", 10)
            .AddSection<DetailsSectionComponent>(DetailsSection, "Details", 10);
    }

    private sealed class OverviewSectionComponent;

    private sealed class DetailsSectionComponent;

    private sealed class ReportsSectionComponent;
}
