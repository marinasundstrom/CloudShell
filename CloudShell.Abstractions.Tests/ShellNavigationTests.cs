using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CoreShell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Abstractions.Tests;

public sealed class ShellNavigationTests
{
    [Fact]
    public void GetHref_UsesComponentTypeAndValidatesRouteValues()
    {
        var navigator = CreateNavigator<ParameterizedViewExtension>();

        var href = navigator.GetHref<ParameterizedPage>(new
        {
            ResourceId = "docker:engine",
            tab = "logs"
        });

        Assert.Equal("/resources/docker%3Aengine/edit?tab=logs", href);
    }

    [Fact]
    public void GetHref_UsesViewIdAndValidatesRouteValues()
    {
        var navigator = CreateNavigator<ExplicitViewIdExtension>();

        var href = navigator.GetHref("sample.parameterized", new Dictionary<string, object?>
        {
            ["ResourceId"] = "configuration:store"
        });

        Assert.Equal("/resources/configuration%3Astore/edit", href);
    }

    [Fact]
    public void GetHref_UsesComponentTypeForViewsRegisteredWithStableIds()
    {
        var navigator = CreateNavigator<ExplicitViewIdExtension>();

        var href = navigator.GetHref<ParameterizedPage>(new
        {
            ResourceId = "docker:engine"
        });

        Assert.Equal("/resources/docker%3Aengine/edit", href);
    }

    [Fact]
    public void GetHref_RejectsMissingRequiredRouteValues()
    {
        var navigator = CreateNavigator<ParameterizedViewExtension>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => navigator.GetHref<ParameterizedPage>());

        Assert.Contains("ResourceId", exception.Message);
    }

    [Fact]
    public void GetHref_AllowsHrefTargetsWithoutRegisteredViews()
    {
        var navigator = CreateNavigator<ParameterizedViewExtension>();

        var href = navigator.GetHref(NavItemTarget.ForHref("https://example.com/docs"));

        Assert.Equal("https://example.com/docs", href);
    }

    [Fact]
    public void NavigateTo_UsesResolvedComponentHref()
    {
        var navigationManager = new TestNavigationManager();
        var navigator = CreateNavigator<ParameterizedViewExtension>(navigationManager);

        navigator.NavigateTo<ParameterizedPage>(
            new { ResourceId = "app:orders" },
            replace: true);

        Assert.Equal("/resources/app%3Aorders/edit", navigationManager.NavigatedTo);
        Assert.True(navigationManager.Options.ReplaceHistoryEntry);
    }

    [Fact]
    public async Task CoreShellExtension_RegistersMainMenuItemsAsCoreShellNavigation()
    {
        var navigation = CreateCoreShellCatalog(new CoreShellExtension());

        var menu = await navigation.GetMenuAsync(ShellIds.MainMenu);

        Assert.NotNull(menu);
        Assert.Collection(
            menu.Groups,
            workspace =>
            {
                Assert.Equal(ShellIds.WorkspaceMenuGroup, workspace.Id);
                var overview = Assert.Single(workspace.Items);
                Assert.Equal(ShellIds.OverviewMenuItem, overview.Id);
                Assert.Equal(ShellIds.OverviewPage.Value, overview.Target.Value);
            });
    }

    [Fact]
    public async Task CoreShellExtension_ResolvesSettingsPageAndNestedSectionTargets()
    {
        var catalog = CreateCoreShellCatalog(
            new CoreShellExtension(),
            new ResourceManagerExtension());
        ICoreShellRouteService routes = catalog;
        ICoreShellSectionService sections = catalog;

        var settings = await routes.ResolveTargetAsync(CoreShellTarget.ForPage(ShellIds.SettingsPage));
        var users = await routes.ResolveTargetAsync(CoreShellTarget.ForSection(ShellIds.SettingsUsersSection));
        var resourceManager = await routes.ResolveTargetAsync(
            CoreShellTarget.ForSection(ResourceManagerShellIds.SettingsGeneralSection));
        var orchestration = await routes.ResolveTargetAsync(
            CoreShellTarget.ForSection(ResourceManagerShellIds.SettingsOrchestrationSection));
        var settingsSections = await sections.GetSectionsAsync(ShellIds.SettingsMainOutlet);

        Assert.Equal("/settings", settings.Href);
        Assert.Equal("/settings/users", users.Href);
        Assert.Equal("/settings/resource-manager", resourceManager.Href);
        Assert.Equal("/settings/resource-manager-orchestration", orchestration.Href);
        Assert.Equal(
            "General",
            settingsSections.Single(section => section.Id == ShellIds.SettingsUsersSection)
                .Attributes[CoreShellAttributeNames.Group]);
        Assert.Equal(
            "Resource Management",
            settingsSections.Single(section => section.Id == ResourceManagerShellIds.SettingsGeneralSection)
                .Attributes[CoreShellAttributeNames.Group]);
        Assert.Equal(
            "Resource Management",
            settingsSections.Single(section => section.Id == ResourceManagerShellIds.SettingsOrchestrationSection)
                .Attributes[CoreShellAttributeNames.Group]);
    }

    [Fact]
    public async Task MainMenu_CanCombineCoreShellExtensionItems()
    {
        var navigation = CreateCoreShellCatalog(
            new CoreShellExtension(),
            new ResourceManagerExtension(includeSettings: false));

        var menu = await navigation.GetMenuAsync(ShellIds.MainMenu);

        Assert.NotNull(menu);
        var workspaceGroup = Assert.Single(menu.Groups, group => group.Id == ShellIds.WorkspaceMenuGroup);
        Assert.Equal(4, workspaceGroup.Items.Count);
        Assert.Contains(workspaceGroup.Items, item => item.Target.Value == ShellIds.OverviewPage.Value);
        Assert.Contains(workspaceGroup.Items, item => item.Target.Value == ResourceManagerShellIds.ResourcesPage.Value);
        Assert.Contains(workspaceGroup.Items, item => item.Target.Value == ResourceManagerShellIds.EnvironmentPage.Value);
        Assert.Contains(workspaceGroup.Items, item => item.Target.Value == ResourceManagerShellIds.HealthPage.Value);
    }

    [Fact]
    public async Task TelemetryExtension_RegistersMainMenuItemsAsCoreShellNavigation()
    {
        var catalog = CreateCoreShellCatalog(new TelemetryExtension());
        ICoreShellRouteService routes = catalog;

        Assert.Equal(
            "/telemetry",
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(TelemetryShellIds.OverviewPage))).Href);
        Assert.Equal(
            "/logs",
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(TelemetryShellIds.LogsPage))).Href);
        Assert.Equal(
            "/telemetry/dependencies",
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(TelemetryShellIds.DependenciesPage))).Href);
        Assert.Equal(
            "/telemetry/service-map",
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(TelemetryShellIds.ServiceMapPage))).Href);
        Assert.Equal(
            "/telemetry/traces",
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(TelemetryShellIds.TracesPage))).Href);
        Assert.Equal(
            "/telemetry/metrics",
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(TelemetryShellIds.MetricsPage))).Href);

        Assert.Equal(
            ObservabilityAuthorization.AnyReadPermissions,
            (await catalog.GetPageAsync(TelemetryShellIds.OverviewPage))?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.LogsReadPermissions,
            (await catalog.GetPageAsync(TelemetryShellIds.LogsPage))?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.TracesReadPermissions,
            (await catalog.GetPageAsync(TelemetryShellIds.DependenciesPage))?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.TracesReadPermissions,
            (await catalog.GetPageAsync(TelemetryShellIds.ServiceMapPage))?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.TracesReadPermissions,
            (await catalog.GetPageAsync(TelemetryShellIds.TracesPage))?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.MetricsReadPermissions,
            (await catalog.GetPageAsync(TelemetryShellIds.MetricsPage))?.Authorization.AnyPermissions);

        var menu = await catalog.GetMenuAsync(ShellIds.MainMenu);
        Assert.NotNull(menu);
        var workspaceGroup = Assert.Single(menu.Groups);
        Assert.Equal(ShellIds.WorkspaceMenuGroup, workspaceGroup.Id);
        Assert.Collection(
            workspaceGroup.Items,
            overview =>
            {
                Assert.Equal(TelemetryShellIds.OverviewMenuItem, overview.Id);
                Assert.Equal(TelemetryShellIds.OverviewPage.Value, overview.Target.Value);
                Assert.Equal(ObservabilityAuthorization.AnyReadPermissions, overview.Authorization.AnyPermissions);
            },
            logs =>
            {
                Assert.Equal(TelemetryShellIds.LogsMenuItem, logs.Id);
                Assert.Equal(TelemetryShellIds.OverviewMenuItem, logs.ParentId);
                Assert.Equal(TelemetryShellIds.LogsPage.Value, logs.Target.Value);
                Assert.Equal(ObservabilityAuthorization.LogsReadPermissions, logs.Authorization.AnyPermissions);
            },
            dependencies =>
            {
                Assert.Equal(TelemetryShellIds.DependenciesMenuItem, dependencies.Id);
                Assert.Equal(TelemetryShellIds.OverviewMenuItem, dependencies.ParentId);
                Assert.Equal(TelemetryShellIds.DependenciesPage.Value, dependencies.Target.Value);
                Assert.Equal(ObservabilityAuthorization.TracesReadPermissions, dependencies.Authorization.AnyPermissions);
            },
            serviceMap =>
            {
                Assert.Equal(TelemetryShellIds.ServiceMapMenuItem, serviceMap.Id);
                Assert.Equal(TelemetryShellIds.OverviewMenuItem, serviceMap.ParentId);
                Assert.Equal(TelemetryShellIds.ServiceMapPage.Value, serviceMap.Target.Value);
                Assert.Equal(ObservabilityAuthorization.TracesReadPermissions, serviceMap.Authorization.AnyPermissions);
            },
            traces =>
            {
                Assert.Equal(TelemetryShellIds.TracesMenuItem, traces.Id);
                Assert.Equal(TelemetryShellIds.OverviewMenuItem, traces.ParentId);
                Assert.Equal(TelemetryShellIds.TracesPage.Value, traces.Target.Value);
                Assert.Equal(ObservabilityAuthorization.TracesReadPermissions, traces.Authorization.AnyPermissions);
            },
            metrics =>
            {
                Assert.Equal(TelemetryShellIds.MetricsMenuItem, metrics.Id);
                Assert.Equal(TelemetryShellIds.OverviewMenuItem, metrics.ParentId);
                Assert.Equal(TelemetryShellIds.MetricsPage.Value, metrics.Target.Value);
                Assert.Equal(ObservabilityAuthorization.MetricsReadPermissions, metrics.Authorization.AnyPermissions);
            });
    }

    [Fact]
    public async Task UsageExtension_RegistersMainMenuItemAsCoreShellNavigation()
    {
        var catalog = CreateCoreShellCatalog(new UsageExtension());
        ICoreShellRouteService routes = catalog;

        Assert.Equal(
            "/usage",
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(UsageShellIds.UsagePage))).Href);
        Assert.Equal(
            UsageAuthorization.UsageReadPermissions,
            (await catalog.GetPageAsync(UsageShellIds.UsagePage))?.Authorization.AnyPermissions);

        var menu = await catalog.GetMenuAsync(ShellIds.MainMenu);
        Assert.NotNull(menu);
        var workspaceGroup = Assert.Single(menu.Groups);
        var item = Assert.Single(workspaceGroup.Items);
        Assert.Equal(UsageShellIds.UsageMenuItem, item.Id);
        Assert.Equal(UsageShellIds.UsagePage.Value, item.Target.Value);
        Assert.Equal(UsageAuthorization.UsageReadPermissions, item.Authorization.AnyPermissions);
    }

    [Fact]
    public async Task ResourceManagerExtension_RegistersStaticShellPagesAsCoreShellPages()
    {
        var catalog = CreateCoreShellCatalog(
            new ResourceManagerExtension(includeSettings: false));
        ICoreShellRouteService routes = catalog;

        Assert.Equal(
            ResourceManagerRoutes.Resources,
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(ResourceManagerShellIds.ResourcesPage))).Href);
        Assert.Equal(
            ResourceManagerRoutes.ResourceGraph,
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(ResourceManagerShellIds.ResourceGraphPage))).Href);
        Assert.Equal(
            "/health",
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(ResourceManagerShellIds.HealthPage))).Href);
        Assert.Equal(
            ResourceManagerRoutes.AddResource,
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(ResourceManagerShellIds.AddResourcePage))).Href);
        Assert.Equal(
            ResourceManagerRoutes.CreateResourceGroup,
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(ResourceManagerShellIds.CreateResourceGroupPage))).Href);
        Assert.Equal(
            ResourceManagerRoutes.ResourceTemplates,
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(ResourceManagerShellIds.ResourceTemplatesPage))).Href);
        Assert.Equal(
            ResourceManagerRoutes.ResourceSettings,
            (await routes.ResolveTargetAsync(CoreShellTarget.ForPage(ResourceManagerShellIds.ResourceSettingsPage))).Href);

        var menu = await catalog.GetMenuAsync(ShellIds.MainMenu);
        Assert.NotNull(menu);
        var workspaceGroup = Assert.Single(menu.Groups);
        Assert.Equal(ShellIds.WorkspaceMenuGroup, workspaceGroup.Id);
        Assert.Collection(
            workspaceGroup.Items,
            item =>
            {
                Assert.Equal(ResourceManagerShellIds.ResourcesMenuItem, item.Id);
                Assert.Equal(ResourceManagerShellIds.ResourcesPage.Value, item.Target.Value);
            },
            item =>
            {
                Assert.Equal(ResourceManagerShellIds.EnvironmentMenuItem, item.Id);
                Assert.Equal(ResourceManagerShellIds.EnvironmentPage.Value, item.Target.Value);
            },
            item =>
            {
                Assert.Equal(ResourceManagerShellIds.HealthMenuItem, item.Id);
                Assert.Equal(ResourceManagerShellIds.HealthPage.Value, item.Target.Value);
            });
    }

    [Fact]
    public void ResourceManagerShellLinks_ResolveDetailsThroughCoreShellRoutes()
    {
        var routes = CreateCoreShellCatalog(
            new ResourceManagerExtension(includeSettings: false));

        Assert.Equal(
            "/resources/application%3Aorders",
            ResourceManagerShellLinks.ResourceDetails(routes, "application:orders"));
        Assert.Equal(
            "/resources/application%3Aorders/logs",
            ResourceManagerShellLinks.ResourceDetails(
                routes,
                "application:orders",
                ResourcePredefinedViewIds.Logs));
    }

    [Fact]
    public void ResourceManagerShellLinks_FallBackWhenCoreShellRouteCannotResolve()
    {
        var routes = new CoreShellModuleCatalog([]);

        Assert.Equal(
            ResourceManagerRoutes.ResourceDetails(
                "application:orders",
                ResourcePredefinedViewIds.Logs),
            ResourceManagerShellLinks.ResourceDetails(
                routes,
                "application:orders",
                ResourcePredefinedViewIds.Logs));
        Assert.Equal(
            ResourceManagerRoutes.Resources,
            ResourceManagerShellLinks.ResourceManagerPage(
                routes,
                ResourceManagerShellIds.ResourcesPage,
                ResourceManagerRoutes.Resources));
    }

    [Fact]
    public void ResourceTabLayoutProjection_UsesCoreShellRoutesForTabHrefs()
    {
        var routes = CreateCoreShellCatalog(
            new ResourceManagerExtension(includeSettings: false));
        var tabs = new[]
        {
            new ResourceTabContribution(
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                typeof(ParameterizedPage)),
            new ResourceTabContribution(
                ResourcePredefinedViewIds.Logs,
                "Logs",
                20,
                typeof(ParameterizedPage))
        };

        var items = ResourceTabLayoutProjection.CreateItems(
            tabs,
            routes,
            "application:orders");

        Assert.Equal(
            "/resources/application%3Aorders",
            items.Single(item => item.Id == ResourcePredefinedViewIds.Overview.Value).Href);
        Assert.Equal(
            "/resources/application%3Aorders/logs",
            items.Single(item => item.Id == ResourcePredefinedViewIds.Logs.Value).Href);
    }

    [Fact]
    public void ResourceTabLayoutProjection_OrdersSemanticGroupsForResourceWorkflow()
    {
        var tabs = new[]
        {
            new ResourceTabContribution(
                ResourcePredefinedViewIds.Overview,
                "Overview",
                0,
                typeof(ParameterizedPage)),
            new ResourceTabContribution(
                new ResourceViewId(ResourceTabGroupIds.Messaging, "broker"),
                "Broker",
                10,
                typeof(ParameterizedPage)),
            new ResourceTabContribution(
                ResourcePredefinedViewIds.Endpoints,
                "Endpoints",
                20,
                typeof(ParameterizedPage)),
            new ResourceTabContribution(
                new ResourceViewId(ResourceTabGroupIds.Application, "deployment"),
                "Deployment",
                15,
                typeof(ParameterizedPage)),
            new ResourceTabContribution(
                ResourcePredefinedViewIds.Storage,
                "Storage",
                30,
                typeof(ParameterizedPage)),
            new ResourceTabContribution(
                ResourcePredefinedViewIds.Activity,
                "Activity",
                40,
                typeof(ParameterizedPage))
        };

        var items = ResourceTabLayoutProjection.CreateItems(tabs);

        Assert.Equal(
            [
                ResourceTabGroupTitles.General,
                ResourceTabGroupTitles.Application,
                ResourceTabGroupTitles.Messaging,
                ResourceTabGroupTitles.Networking,
                ResourceTabGroupTitles.Storage,
                ResourceTabGroupTitles.Management
            ],
            items
                .Select(item => item.GroupTitle ?? string.Empty)
                .Distinct()
                .ToArray());
    }

    [Fact]
    public void ResourceTabLayoutProjection_OrdersTabsWithinGroupByContributionOrder()
    {
        var tabs = new[]
        {
            new ResourceTabContribution(
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                typeof(ParameterizedPage)),
            new ResourceTabContribution(
                ResourcePredefinedViewIds.Overview,
                "Overview",
                100,
                typeof(ParameterizedPage)),
            new ResourceTabContribution(
                new ResourceViewId(ResourceTabGroupIds.General, "settings"),
                "Settings",
                10,
                typeof(ParameterizedPage))
        };

        var items = ResourceTabLayoutProjection.CreateItems(tabs);

        Assert.Equal(
            [
                ResourcePredefinedViewIds.Overview.Value,
                "general:settings",
                ResourcePredefinedViewIds.Configuration.Value
            ],
            items.Select(item => item.Id).ToArray());
    }

    private static ICloudShellNavigator CreateNavigator<TExtension>(
        TestNavigationManager? navigationManager = null)
        where TExtension : class, ICloudShellExtension, new()
    {
        return new CloudShellNavigator(
            CreateShellCatalog<TExtension>(),
            navigationManager ?? new TestNavigationManager());
    }

    private static ShellCatalog CreateShellCatalog<TExtension>()
        where TExtension : class, ICloudShellExtension, new()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellUi()
            .AddExtension<TExtension>();

        var registry = Assert.IsType<CloudShellExtensionRegistry>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry))
                .ImplementationInstance);
        registry.Validate();

        return new ShellCatalog(registry, new InMemoryCloudShellExtensionActivationStore());
    }

    private static CoreShellModuleCatalog CreateCoreShellCatalog(
        params ICloudShellExtension[] extensions)
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<ShellHostContext>();

        var builder = services.AddCloudShellUi();
        foreach (var extension in extensions)
        {
            builder.AddExtension(extension);
        }

        using var serviceProvider = services.BuildServiceProvider();
        return new CoreShellModuleCatalog(serviceProvider.GetServices<CoreShellModule>());
    }

    private sealed class ParameterizedViewExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.parameterized",
            "Sample parameterized",
            "Registers a parameterized view.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.RegisterView<ParameterizedPage>();
        }
    }

    private sealed class ExplicitViewIdExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.explicit-parameterized",
            "Sample explicit parameterized",
            "Registers a parameterized view with an explicit id.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.RegisterView<ParameterizedPage>("sample.parameterized");
        }
    }

    [Route("/resources/{ResourceId}/edit")]
    private sealed class ParameterizedPage;

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("https://cloudshell.test/", "https://cloudshell.test/");
        }

        public string? NavigatedTo { get; private set; }

        public NavigationOptions Options { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            NavigatedTo = uri;
            Options = options;
        }
    }
}
