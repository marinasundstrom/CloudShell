using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Tests;

public sealed class ExtensionRegistrationTests
{
    [Fact]
    public void AddExtension_RecordsManifestAndNormalizesViewRoute()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ProviderExtension>();

        var registry = GetRegistry(services);
        var extension = Assert.Single(registry.Extensions);
        var view = Assert.Single(extension.Views);

        Assert.Equal("sample.provider", extension.Id);
        Assert.Equal("1.2.3", extension.Version);
        Assert.Equal("/sample", view.Route);
    }

    [Fact]
    public void AddExtension_RegistersResourceProvidersInTheServiceCollection()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ProviderExtension>();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(SampleResourceProvider));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IResourceProvider) &&
            descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void AddResourceType_RecordsSeparateRegistrationAndUpdateComponents()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ResourceProcedureExtension>();

        var registry = GetRegistry(services);
        var resourceType = Assert.Single(
            Assert.Single(registry.Extensions).ResourceTypes);

        Assert.Equal("sample.resource", resourceType.Id);
        Assert.Equal(typeof(SampleRegistrationPage), resourceType.RegistrationComponentType);
        Assert.Equal(typeof(SampleUpdatePage), resourceType.UpdateComponentType);
    }

    [Fact]
    public void AddResourceTab_RecordsTabsForResourceType()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ResourceTabsExtension>();

        var registry = GetRegistry(services);
        var resourceType = Assert.Single(
            Assert.Single(registry.Extensions).ResourceTypes);
        var tabs = resourceType.ResourceTabs;

        Assert.Collection(
            tabs,
            tab =>
            {
                Assert.Equal("overview", tab.Id);
                Assert.Equal(typeof(SampleOverviewPage), tab.ComponentType);
                Assert.False(tab.ShowsApplyButton);
            },
            tab =>
            {
                Assert.Equal("configuration", tab.Id);
                Assert.Equal(typeof(SampleUpdatePage), tab.ComponentType);
                Assert.True(tab.ShowsApplyButton);
            });
    }

    [Fact]
    public void Validate_RejectsDuplicateRoutes()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ProviderExtension>()
            .AddExtension<DuplicateRouteExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("/sample", exception.Message);
    }

    [Fact]
    public void Validate_RejectsMissingCapabilities()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<MissingDependencyExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("sample.missing", exception.Message);
    }

    [Fact]
    public void AddExtension_RejectsDuplicateExtensionIds()
    {
        var services = new ServiceCollection();
        var builder = services.AddCloudShell().AddExtension<ProviderExtension>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddExtension<ProviderExtension>());

        Assert.Contains("sample.provider", exception.Message);
    }

    private static CloudShellExtensionRegistry GetRegistry(IServiceCollection services) =>
        Assert.IsType<CloudShellExtensionRegistry>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry))
                .ImplementationInstance);

    private sealed class ProviderExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.provider",
            "Sample provider",
            "A test extension.",
            "1.2.3",
            ["sample.resources"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddView<SamplePage>("Sample", "sample", "sample", 10)
                .AddResourceProvider<SampleResourceProvider>();
        }
    }

    private sealed class DuplicateRouteExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.duplicate-route",
            "Duplicate route",
            "Contributes a conflicting route.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddView<DuplicatePage>("Duplicate", "/sample", "sample", 20);
        }
    }

    private sealed class MissingDependencyExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.consumer",
            "Sample consumer",
            "Requires a capability that is not installed.",
            "1.0.0",
            [],
            ["sample.missing"]);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
        }
    }

    private sealed class ResourceProcedureExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.procedures",
            "Sample procedures",
            "Contributes resource procedures.",
            "1.0.0",
            ["sample.resource"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddResourceType<SampleRegistrationPage, SampleUpdatePage>(
                "sample.resource",
                "Sample resource",
                "A resource with provider-owned procedures.",
                "sample",
                10);
        }
    }

    private sealed class ResourceTabsExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.tabs",
            "Sample tabs",
            "Contributes resource tabs.",
            "1.0.0",
            ["sample.tabs"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddResourceType<SampleRegistrationPage>(
                    "sample.tabs",
                    "Sample tabs",
                    "A resource with tabs.",
                    "sample",
                    10)
                .AddResourceTab<SampleUpdatePage>(
                    "sample.tabs",
                    "configuration",
                    "Configuration",
                    20,
                    showsApplyButton: true)
                .AddResourceTab<SampleOverviewPage>(
                    "sample.tabs",
                    "overview",
                    "Overview",
                    10);
        }
    }

    private sealed class SampleResourceProvider : IResourceProvider
    {
        public string Id => "sample";

        public string DisplayName => "Sample";

        public IReadOnlyList<CloudResource> GetResources() => [];
    }

    private sealed class SamplePage;

    private sealed class DuplicatePage;

    private sealed class SampleRegistrationPage;

    private sealed class SampleUpdatePage;

    private sealed class SampleOverviewPage;
}
