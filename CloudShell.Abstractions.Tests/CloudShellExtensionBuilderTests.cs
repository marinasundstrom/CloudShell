using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Tests;

public sealed class CloudShellExtensionBuilderTests
{
    [Fact]
    public void AddResourceTab_RejectsDuplicatePublicRouteSegment()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ConfigureExtension(builder => builder
                .AddResourceType<TestComponent>("sample.resource", "Sample", "Sample resource.", "sample", 10)
                .AddResourceTab<TestComponent>(
                    "sample.resource",
                    new ResourceViewId(ResourceTabGroupIds.General, "settings"),
                    "Settings",
                    10)
                .AddResourceTab<TestComponent>(
                    "sample.resource",
                    new ResourceViewId(ResourceTabGroupIds.Management, "SETTINGS"),
                    "Managed settings",
                    20)));

        Assert.Equal(
            "Resource type 'sample.resource' already has resource view 'general:settings' using public route segment 'settings'. Resource view route segments must be unique within a resource type.",
            exception.Message);
    }

    [Fact]
    public void AddResourceTab_RejectsCustomIdUsingPredefinedPublicRouteSegment()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ConfigureExtension(builder => builder
                .AddResourceType<TestComponent>("sample.resource", "Sample", "Sample resource.", "sample", 10)
                .AddResourceTab<TestComponent>(
                    "sample.resource",
                    new ResourceViewId(ResourceTabGroupIds.Application, "logs"),
                    "Application logs",
                    10)));

        Assert.Equal(
            "Resource type 'sample.resource' cannot contribute resource view 'application:logs' using public route segment 'logs' because that segment is reserved by predefined resource view 'telemetry:logs'. Reuse the predefined view ID to replace that view.",
            exception.Message);
    }

    [Fact]
    public void AddResourceTab_AllowsExactPredefinedViewReplacement()
    {
        var extension = ConfigureExtension(builder => builder
            .AddResourceType<TestComponent>("sample.resource", "Sample", "Sample resource.", "sample", 10)
            .AddResourceTab<TestComponent>(
                "sample.resource",
                ResourcePredefinedViewIds.Logs,
                "Provider logs",
                10));

        var resourceType = Assert.Single(extension.ResourceTypes);
        var tab = Assert.Single(resourceType.ResourceTabs);

        Assert.Equal(ResourcePredefinedViewIds.Logs, tab.Id);
    }

    [Fact]
    public void AddResourceTab_AllowsDistinctPublicRouteSegments()
    {
        var extension = ConfigureExtension(builder => builder
            .AddResourceType<TestComponent>("sample.resource", "Sample", "Sample resource.", "sample", 10)
            .AddResourceTab<TestComponent>(
                "sample.resource",
                new ResourceViewId(ResourceTabGroupIds.General, "settings"),
                "Settings",
                10)
            .AddResourceTab<TestComponent>(
                "sample.resource",
                new ResourceViewId(ResourceTabGroupIds.Management, "diagnostics"),
                "Diagnostics",
                20));

        var resourceType = Assert.Single(extension.ResourceTypes);

        Assert.Equal(2, resourceType.ResourceTabs.Count);
    }

    private static CloudShellExtensionRegistration ConfigureExtension(
        Action<ICloudShellExtensionBuilder> configure)
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellUi()
            .AddExtension(new TestExtension(configure));

        var registry = Assert.IsType<CloudShellExtensionRegistry>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry))
                .ImplementationInstance);

        return Assert.Single(registry.Extensions);
    }

    private sealed class TestExtension(Action<ICloudShellExtensionBuilder> configure) : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest { get; } = new(
            "sample.extension",
            "Sample extension",
            "Sample extension.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder) => configure(builder);
    }

    private sealed class TestComponent;
}
