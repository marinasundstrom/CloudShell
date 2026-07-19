using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceTabComponentParameterResolverTests
{
    [Fact]
    public void Resolve_ProjectsGeneratedLogViewContext()
    {
        var resource = CreateResource() with { Observability = ResourceObservability.Default };
        var resolution = ResourceTabResolver.Resolve(
            resource,
            resourceType: null,
            hasResourceLogs: true,
            hasResourceMonitoring: false,
            canReadUsage: false,
            hasDefaultIdentityProvider: false);
        var tab = resolution.Tabs.Single(tab => tab.Id == ResourcePredefinedViewIds.Logs);
        var logSources = new[]
        {
            new LogSource(
                "console",
                "Console",
                "test",
                "console",
                LogSourceKind.Resource,
                ResourceId: resource.Id)
        };

        var parameters = ResourceTabComponentParameterResolver.Resolve(
            new ResourceTabComponentContext(
                resource,
                ResourceType: null,
                tab,
                resolution,
                TraceId: "trace-1",
                LogSourceId: "console",
                LogView: "stream",
                LogSourceIds: "console,activity",
                LogSources: logSources));

        Assert.Equal(resource.Id, parameters["ResourceId"]);
        Assert.Equal("console", parameters["LogSourceId"]);
        Assert.Equal("trace-1", parameters["TraceId"]);
        Assert.Equal("stream", parameters["View"]);
        Assert.Equal("console,activity", parameters["SourceIds"]);
        Assert.Same(logSources, parameters["LogSources"]);
        Assert.DoesNotContain("ResourceType", parameters);
    }

    [Fact]
    public void Resolve_ProjectsResourceTypeContextForGeneratedActivityView()
    {
        var resource = CreateResource();
        var resourceType = CreateResourceType();
        var resolution = ResourceTabResolver.Resolve(
            resource,
            resourceType,
            hasResourceLogs: false,
            hasResourceMonitoring: false,
            canReadUsage: false,
            hasDefaultIdentityProvider: false);
        var tab = resolution.Tabs.Single(tab => tab.Id == ResourcePredefinedViewIds.Activity);

        var parameters = ResourceTabComponentParameterResolver.Resolve(
            new ResourceTabComponentContext(
                resource,
                resourceType,
                tab,
                resolution,
                TraceId: "trace-1",
                SpanId: "span-1"));

        Assert.Equal(resource.Id, parameters["ResourceId"]);
        Assert.Same(resourceType, parameters["ResourceType"]);
        Assert.Equal("trace-1", parameters["TraceId"]);
        Assert.Equal("span-1", parameters["SpanId"]);
    }

    [Fact]
    public void Resolve_GivesProviderReplacementOnlyResourceContext()
    {
        var resource = CreateResource() with { Observability = ResourceObservability.Default };
        var providerLogs = new ResourceTabContribution(
            ResourcePredefinedViewIds.Logs,
            "Provider logs",
            10,
            typeof(ProviderLogsView));
        var resourceType = CreateResourceType([providerLogs]);
        var resolution = ResourceTabResolver.Resolve(
            resource,
            resourceType,
            hasResourceLogs: true,
            hasResourceMonitoring: false,
            canReadUsage: false,
            hasDefaultIdentityProvider: false);

        var parameters = ResourceTabComponentParameterResolver.Resolve(
            new ResourceTabComponentContext(
                resource,
                resourceType,
                providerLogs,
                resolution,
                TraceId: "trace-1",
                LogSourceId: "console",
                ScopeResourceId: "resource:scope"));

        var parameter = Assert.Single(parameters);
        Assert.Equal("ResourceId", parameter.Key);
        Assert.Equal(resource.Id, parameter.Value);
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
        IReadOnlyList<ResourceTabContribution>? tabs = null) =>
        new(
            "test.resource",
            "Test Resource",
            "Test resource.",
            "resource",
            0,
            typeof(object),
            Tabs: tabs);

    private sealed class ProviderLogsView;
}
