using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Shell;
using CloudShell.Hosting.Shell;
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

    private static ICloudShellNavigator CreateNavigator<TExtension>(
        TestNavigationManager? navigationManager = null)
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

        return new CloudShellNavigator(
            new ShellCatalog(registry, new InMemoryCloudShellExtensionActivationStore()),
            navigationManager ?? new TestNavigationManager());
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
