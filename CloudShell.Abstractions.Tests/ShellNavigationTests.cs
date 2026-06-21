using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.UI.Composition;
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
    public void ShellNavigationCompositionProjector_ProjectsNavigationItemsIntoCompositionMenu()
    {
        var shellCatalog = CreateShellCatalog<NavigationCompositionExtension>();
        var module = new ShellNavigationCompositionProjector(shellCatalog).CreateModule();
        var registry = CompositionRegistry.FromModules(module);

        var menu = registry.GetMenu(ShellCompositionIds.MainMenu);
        Assert.NotNull(menu);
        var workspaceGroup = Assert.Single(
            menu.Groups,
            group => group.Title == "Workspace");
        var observabilityGroup = Assert.Single(
            menu.Groups,
            group => group.Title == "Observability");
        var workspaceItem = Assert.Single(
            workspaceGroup.Items,
            item => item.Title == "Workspace");
        var tracesItem = Assert.Single(
            observabilityGroup.Items,
            item => item.Title == "Traces");

        Assert.Equal("grid", workspaceItem.Attributes[CompositionAttributeNames.Icon]);
        Assert.Equal("/workspace", workspaceItem.Target.Value);
        Assert.Equal("trace", tracesItem.Attributes[CompositionAttributeNames.Icon]);
        Assert.Equal(workspaceItem.Id, tracesItem.ParentId);
        Assert.Equal(["observability.read"], tracesItem.PermissionsRequiredForNavigation);
        Assert.Equal("/observability/traces", registry.ResolveHref(tracesItem.Target));
    }

    [Fact]
    public void CoreShellExtension_RegistersMainMenuItemsAsCompositionMenu()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShell()
            .AddExtension<CoreShellExtension>();
        var module = services
            .Where(descriptor => descriptor.ServiceType == typeof(CompositionModule))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CompositionModule>()
            .Single(module => module.Id == ShellCompositionIds.CoreModule);
        var registry = CompositionRegistry.FromModules(module);

        var menu = registry.GetMenu(ShellCompositionIds.MainMenu);
        Assert.NotNull(menu);
        Assert.Collection(
            menu.Groups,
            workspace =>
            {
                Assert.Equal(ShellCompositionIds.WorkspaceMenuGroup, workspace.Id);
                var overview = Assert.Single(workspace.Items);
                Assert.Equal(ShellCompositionIds.OverviewMenuItem, overview.Id);
                Assert.Equal(ShellCompositionIds.OverviewPage.Value, overview.Target.Value);
            });
    }

    [Fact]
    public void CoreShellExtension_ResolvesSettingsPageAndNestedSectionRoutes()
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<ShellCompositionHostContext>();
        services
            .AddCloudShell()
            .AddExtension<CoreShellExtension>()
            .AddExtension(new ResourceManagerExtension());

        using var serviceProvider = services.BuildServiceProvider();
        var registry = CompositionRegistry.FromModules(serviceProvider.GetServices<CompositionModule>());

        Assert.Equal("/settings", registry.ResolveHref(ShellCompositionIds.SettingsPage));
        Assert.Equal(
            "/settings/users",
            registry.ResolveHref(
                ShellCompositionIds.SettingsPage,
                new Dictionary<string, object?>
                {
                    ["section"] = "users"
                }));
        Assert.Equal(
            "/settings/resource-manager",
            registry.ResolveHref(
                ShellCompositionIds.SettingsPage,
                new Dictionary<string, object?>
                {
                    ["section"] = "resource-manager"
                }));
        Assert.Equal(
            CompositionSectionAddressMode.Child,
            registry.GetSectionOutlet(ShellCompositionIds.SettingsMainOutlet)?.AddressMode);
        Assert.Equal(
            "/settings/users",
            registry.ResolveHref(ShellCompositionIds.SettingsUsersSection));
        Assert.Equal(
            "/settings/extensions",
            registry.ResolveHref(ShellCompositionIds.SettingsExtensionsSection));
        Assert.Equal(
            "/settings/resource-manager",
            registry.ResolveHref(ResourceManagerCompositionIds.SettingsSection));
    }

    [Fact]
    public void MainMenu_CanCombineProjectedLegacyItemsAndNativeCompositionItems()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShell()
            .AddExtension<CoreShellExtension>()
            .AddExtension(new ResourceManagerExtension(includeSettings: false));
        var registry = Assert.IsType<CloudShellExtensionRegistry>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry))
                .ImplementationInstance);
        registry.Validate();
        var shellCatalog = new ShellCatalog(registry, new InMemoryCloudShellExtensionActivationStore());
        var navigationModule = new ShellNavigationCompositionProjector(shellCatalog).CreateModule();
        var compositionModules = services
            .Where(descriptor => descriptor.ServiceType == typeof(CompositionModule))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CompositionModule>()
            .Append(navigationModule)
            .ToArray();
        var composition = CompositionRegistry.FromModules(compositionModules);

        var menu = composition.GetMenu(ShellCompositionIds.MainMenu);
        Assert.NotNull(menu);
        var workspaceGroup = Assert.Single(menu.Groups, group => group.Id == ShellCompositionIds.WorkspaceMenuGroup);
        Assert.Equal(3, workspaceGroup.Items.Count);
        Assert.Contains(workspaceGroup.Items, item => item.Target.Value == ShellCompositionIds.OverviewPage.Value);
        Assert.Contains(workspaceGroup.Items, item => item.Target.Value == ResourceManagerCompositionIds.ResourcesPage.Value);
        Assert.Contains(workspaceGroup.Items, item => item.Target.Value == ResourceManagerCompositionIds.HealthPage.Value);
    }

    [Fact]
    public void ObservabilityExtension_RegistersMainMenuItemsAsCompositionMenu()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShell()
            .AddExtension<ObservabilityExtension>();
        var module = services
            .Where(descriptor => descriptor.ServiceType == typeof(CompositionModule))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CompositionModule>()
            .Single(module => module.Id == ObservabilityCompositionIds.Module);
        var registry = CompositionRegistry.FromModules(module);

        Assert.Equal("/observability", registry.ResolveHref(ObservabilityCompositionIds.OverviewPage));
        Assert.Equal("/logs", registry.ResolveHref(ObservabilityCompositionIds.LogsPage));
        Assert.Equal("/observability/dependencies", registry.ResolveHref(ObservabilityCompositionIds.DependenciesPage));
        Assert.Equal("/observability/service-map", registry.ResolveHref(ObservabilityCompositionIds.ServiceMapPage));
        Assert.Equal("/observability/traces", registry.ResolveHref(ObservabilityCompositionIds.TracesPage));
        Assert.Equal("/observability/metrics", registry.ResolveHref(ObservabilityCompositionIds.MetricsPage));
        Assert.Equal(
            ObservabilityAuthorization.AnyReadPermissions,
            registry.GetPage(ObservabilityCompositionIds.OverviewPage)?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.LogsReadPermissions,
            registry.GetPage(ObservabilityCompositionIds.LogsPage)?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.TracesReadPermissions,
            registry.GetPage(ObservabilityCompositionIds.DependenciesPage)?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.TracesReadPermissions,
            registry.GetPage(ObservabilityCompositionIds.ServiceMapPage)?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.TracesReadPermissions,
            registry.GetPage(ObservabilityCompositionIds.TracesPage)?.Authorization.AnyPermissions);
        Assert.Equal(
            ObservabilityAuthorization.MetricsReadPermissions,
            registry.GetPage(ObservabilityCompositionIds.MetricsPage)?.Authorization.AnyPermissions);

        var menu = registry.GetMenu(ShellCompositionIds.MainMenu);
        Assert.NotNull(menu);
        var workspaceGroup = Assert.Single(menu.Groups);
        Assert.Equal(ShellCompositionIds.WorkspaceMenuGroup, workspaceGroup.Id);
        Assert.Collection(
            workspaceGroup.Items,
            overview =>
            {
                Assert.Equal(ObservabilityCompositionIds.OverviewMenuItem, overview.Id);
                Assert.Equal(ObservabilityCompositionIds.OverviewPage.Value, overview.Target.Value);
                Assert.Equal(ObservabilityAuthorization.AnyReadPermissions, overview.Authorization.AnyPermissions);
            },
            logs =>
            {
                Assert.Equal(ObservabilityCompositionIds.LogsMenuItem, logs.Id);
                Assert.Equal(ObservabilityCompositionIds.OverviewMenuItem, logs.ParentId);
                Assert.Equal(ObservabilityCompositionIds.LogsPage.Value, logs.Target.Value);
                Assert.Equal(ObservabilityAuthorization.LogsReadPermissions, logs.Authorization.AnyPermissions);
            },
            dependencies =>
            {
                Assert.Equal(ObservabilityCompositionIds.DependenciesMenuItem, dependencies.Id);
                Assert.Equal(ObservabilityCompositionIds.OverviewMenuItem, dependencies.ParentId);
                Assert.Equal(ObservabilityCompositionIds.DependenciesPage.Value, dependencies.Target.Value);
                Assert.Equal(ObservabilityAuthorization.TracesReadPermissions, dependencies.Authorization.AnyPermissions);
            },
            serviceMap =>
            {
                Assert.Equal(ObservabilityCompositionIds.ServiceMapMenuItem, serviceMap.Id);
                Assert.Equal(ObservabilityCompositionIds.OverviewMenuItem, serviceMap.ParentId);
                Assert.Equal(ObservabilityCompositionIds.ServiceMapPage.Value, serviceMap.Target.Value);
                Assert.Equal(ObservabilityAuthorization.TracesReadPermissions, serviceMap.Authorization.AnyPermissions);
            },
            traces =>
            {
                Assert.Equal(ObservabilityCompositionIds.TracesMenuItem, traces.Id);
                Assert.Equal(ObservabilityCompositionIds.OverviewMenuItem, traces.ParentId);
                Assert.Equal(ObservabilityCompositionIds.TracesPage.Value, traces.Target.Value);
                Assert.Equal(ObservabilityAuthorization.TracesReadPermissions, traces.Authorization.AnyPermissions);
            },
            metrics =>
            {
                Assert.Equal(ObservabilityCompositionIds.MetricsMenuItem, metrics.Id);
                Assert.Equal(ObservabilityCompositionIds.OverviewMenuItem, metrics.ParentId);
                Assert.Equal(ObservabilityCompositionIds.MetricsPage.Value, metrics.Target.Value);
                Assert.Equal(ObservabilityAuthorization.MetricsReadPermissions, metrics.Authorization.AnyPermissions);
            });
    }

    [Fact]
    public void ResourceManagerExtension_RegistersStaticShellPagesAsCompositionPages()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShell()
            .AddExtension(new ResourceManagerExtension(includeSettings: false));

        var module = services
            .Where(descriptor => descriptor.ServiceType == typeof(CompositionModule))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CompositionModule>()
            .Single(module => module.Id == ResourceManagerCompositionIds.Module);
        var registry = CompositionRegistry.FromModules(module);

        Assert.Equal(
            ResourceManagerRoutes.Resources,
            registry.ResolveHref(ResourceManagerCompositionIds.ResourcesPage));
        Assert.Equal(
            ResourceManagerRoutes.ResourceGraph,
            registry.ResolveHref(ResourceManagerCompositionIds.ResourceGraphPage));
        Assert.Equal(
            "/health",
            registry.ResolveHref(ResourceManagerCompositionIds.HealthPage));
        Assert.Equal(
            ResourceManagerRoutes.AddResource,
            registry.ResolveHref(ResourceManagerCompositionIds.AddResourcePage));
        Assert.Equal(
            ResourceManagerRoutes.CreateResourceGroup,
            registry.ResolveHref(ResourceManagerCompositionIds.CreateResourceGroupPage));
        Assert.Equal(
            ResourceManagerRoutes.ResourceTemplates,
            registry.ResolveHref(ResourceManagerCompositionIds.ResourceTemplatesPage));
        Assert.Equal(
            ResourceManagerRoutes.ResourceSettings,
            registry.ResolveHref(ResourceManagerCompositionIds.ResourceSettingsPage));

        var menu = registry.GetMenu(ShellCompositionIds.MainMenu);
        Assert.NotNull(menu);
        var workspaceGroup = Assert.Single(menu.Groups);
        Assert.Equal(ShellCompositionIds.WorkspaceMenuGroup, workspaceGroup.Id);
        Assert.Collection(
            workspaceGroup.Items,
            item =>
            {
                Assert.Equal(ResourceManagerCompositionIds.ResourcesMenuItem, item.Id);
                Assert.Equal(ResourceManagerCompositionIds.ResourcesPage.Value, item.Target.Value);
            },
            item =>
            {
                Assert.Equal(ResourceManagerCompositionIds.HealthMenuItem, item.Id);
                Assert.Equal(ResourceManagerCompositionIds.HealthPage.Value, item.Target.Value);
            });
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
            .AddCloudShell()
            .AddExtension<TExtension>();

        var registry = Assert.IsType<CloudShellExtensionRegistry>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry))
                .ImplementationInstance);
        registry.Validate();

        return new ShellCatalog(registry, new InMemoryCloudShellExtensionActivationStore());
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

    private sealed class NavigationCompositionExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.navigation-composition",
            "Sample navigation composition",
            "Registers navigation items that can be projected into composition.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .RegisterView<WorkspacePage>("sample.workspace")
                .AddNavigationItem<WorkspacePage>("workspace", "Workspace", "grid", 10)
                .RegisterView<TracesPage>("sample.traces")
                .AddNavigationItem<TracesPage>(
                    "traces",
                    "Traces",
                    "trace",
                    20,
                    "Observability",
                    parentId: "workspace",
                    requiredPermissions: ["observability.read"]);
        }
    }

    [Route("/resources/{ResourceId}/edit")]
    private sealed class ParameterizedPage;

    [Route("/workspace")]
    private sealed class WorkspacePage;

    [Route("/observability/traces")]
    private sealed class TracesPage;

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
