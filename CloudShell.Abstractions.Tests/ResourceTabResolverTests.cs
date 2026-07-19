using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.Components.Pages.Resources;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceTabResolverTests
{
    [Fact]
    public void Resolve_AddsCapabilityAndAvailabilityDrivenResourceManagerViews()
    {
        var resource = CreateResource() with
        {
            Endpoints = [ResourceEndpoint.Http("http", "localhost", 5000)],
            HealthChecks = [new ResourceHealthCheck("/health")],
            Observability = ResourceObservability.Default,
            Capabilities =
            [
                new(ResourceCapabilityIds.EnvironmentVariables),
                new(ResourceCapabilityIds.StorageProvider),
                new(ResourceCapabilityIds.Recovery)
            ]
        };

        var resolution = ResourceTabResolver.Resolve(
            resource,
            resourceType: null,
            hasResourceLogs: true,
            hasResourceMonitoring: true,
            canReadUsage: true,
            hasDefaultIdentityProvider: true);

        Assert.Equal(
            [
                ResourcePredefinedViewIds.Overview,
                ResourcePredefinedViewIds.Endpoints,
                ResourcePredefinedViewIds.Dns,
                ResourcePredefinedViewIds.Identity,
                ResourcePredefinedViewIds.AccessControl,
                ResourcePredefinedViewIds.Volumes,
                ResourcePredefinedViewIds.Activity,
                ResourcePredefinedViewIds.Health,
                ResourcePredefinedViewIds.Recovery,
                ResourcePredefinedViewIds.Monitoring,
                ResourcePredefinedViewIds.Usage,
                ResourcePredefinedViewIds.Environment,
                ResourcePredefinedViewIds.Logs,
                ResourcePredefinedViewIds.Traces,
                ResourcePredefinedViewIds.Metrics
            ],
            resolution.Tabs.Select(tab => tab.Id).ToArray());
        Assert.All(resolution.Tabs, tab => Assert.True(resolution.IsGenerated(tab.Id)));
        Assert.True(resolution.AcceptsResourceTypeContext(ResourcePredefinedViewIds.Health));
        Assert.False(resolution.AcceptsResourceTypeContext(ResourcePredefinedViewIds.Logs));
    }

    [Fact]
    public void Resolve_UsesProviderReplacementWithoutMarkingItAsGenerated()
    {
        var providerEndpoints = new ResourceTabContribution(
            ResourcePredefinedViewIds.Endpoints,
            "Provider endpoints",
            10,
            typeof(ProviderEndpointsView));
        var resourceType = CreateResourceType(tabs: [providerEndpoints]);
        var resource = CreateResource() with
        {
            Endpoints = [ResourceEndpoint.Http("http", "localhost", 5000)]
        };

        var resolution = ResourceTabResolver.Resolve(
            resource,
            resourceType,
            hasResourceLogs: false,
            hasResourceMonitoring: false,
            canReadUsage: false,
            hasDefaultIdentityProvider: false);

        var endpoints = Assert.Single(
            resolution.Tabs,
            tab => tab.Id == ResourcePredefinedViewIds.Endpoints);
        Assert.Same(providerEndpoints, endpoints);
        Assert.False(resolution.IsGenerated(ResourcePredefinedViewIds.Endpoints));
        Assert.False(resolution.AcceptsResourceTypeContext(ResourcePredefinedViewIds.Endpoints));
        Assert.True(resolution.IsGenerated(ResourcePredefinedViewIds.Overview));
    }

    [Fact]
    public void Resolve_AddsGeneratedConfigurationForProviderUpdateComponent()
    {
        var resourceType = CreateResourceType(updateComponentType: typeof(UpdateResourceView));

        var resolution = ResourceTabResolver.Resolve(
            CreateResource(),
            resourceType,
            hasResourceLogs: false,
            hasResourceMonitoring: false,
            canReadUsage: false,
            hasDefaultIdentityProvider: false);

        var configuration = Assert.Single(
            resolution.Tabs,
            tab => tab.Id == ResourcePredefinedViewIds.Configuration);
        Assert.Equal(typeof(UpdateResourceView), configuration.ComponentType);
        Assert.True(configuration.ShowsApplyButton);
        Assert.True(resolution.IsGenerated(configuration.Id));
    }

    private static Resource CreateResource() =>
        new(
            "resource:api",
            "api",
            "Generic",
            "Test",
            "local",
            null,
            [],
            "1.0.0",
            DateTimeOffset.UtcNow,
            []);

    private static ResourceTypeContribution CreateResourceType(
        Type? updateComponentType = null,
        IReadOnlyList<ResourceTabContribution>? tabs = null) =>
        new(
            "test.resource",
            "Test Resource",
            "Test resource.",
            "resource",
            0,
            typeof(object),
            UpdateComponentType: updateComponentType,
            Tabs: tabs);

    private sealed class ProviderEndpointsView;

    private sealed class UpdateResourceView;
}
