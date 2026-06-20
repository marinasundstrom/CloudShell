using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.UI.Composition;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

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
    public void ShellNavigationCompositionProjector_TargetsCoreShellViewsByPageId()
    {
        var shellCatalog = CreateShellCatalog<CoreShellExtension>();
        var module = new ShellNavigationCompositionProjector(shellCatalog).CreateModule();
        var registry = CompositionRegistry.FromModules(module);

        var menu = registry.GetMenu(ShellCompositionIds.MainMenu);
        Assert.NotNull(menu);
        var items = menu.Groups
            .SelectMany(group => group.Items)
            .ToDictionary(item => item.Title, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(ShellCompositionIds.OverviewPage.Value, items["Overview"].Target.Value);
        Assert.Equal(ShellCompositionIds.SettingsPage.Value, items["Settings"].Target.Value);
        Assert.Equal(ShellCompositionIds.UsersPage.Value, items["Users"].Target.Value);
        Assert.Equal(ShellCompositionIds.ExtensionsPage.Value, items["Extensions"].Target.Value);
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
            "/resources/add",
            registry.ResolveHref(ResourceManagerCompositionIds.AddResourcePage));
        Assert.Equal(
            "/resources/groups/new",
            registry.ResolveHref(ResourceManagerCompositionIds.CreateResourceGroupPage));
        Assert.Equal(
            "/resources/templates",
            registry.ResolveHref(ResourceManagerCompositionIds.ResourceTemplatesPage));
        Assert.Equal(
            "/resources/settings",
            registry.ResolveHref(ResourceManagerCompositionIds.ResourceSettingsPage));
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
