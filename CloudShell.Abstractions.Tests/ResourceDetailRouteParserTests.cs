using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDetailRouteParserTests
{
    [Fact]
    public void TrySplitCombinedRoute_ParsesEncodedCanonicalViewSuffix()
    {
        var parsed = ResourceDetailRouteParser.TrySplitCombinedRoute(
            "application:api/telemetry%3Alogs",
            out var route);

        Assert.True(parsed);
        Assert.Equal("application:api", route.ResourceId);
        Assert.Equal("telemetry:logs", route.RequestedView);
    }

    [Fact]
    public void TrySplitCombinedRoute_PreservesHierarchicalResourceId()
    {
        var parsed = ResourceDetailRouteParser.TrySplitCombinedRoute(
            "/subscriptions/local/resourceGroups/demo/apps/orders/networking%3Aendpoints",
            out var route);

        Assert.True(parsed);
        Assert.Equal(
            "/subscriptions/local/resourceGroups/demo/apps/orders",
            route.ResourceId);
        Assert.Equal("networking:endpoints", route.RequestedView);
    }

    [Theory]
    [InlineData("application:api")]
    [InlineData("application:api/")]
    [InlineData("application:api/logs")]
    [InlineData("/telemetry%3Alogs")]
    public void TrySplitCombinedRoute_RejectsNonCanonicalOrIncompleteRoute(string value)
    {
        Assert.False(ResourceDetailRouteParser.TrySplitCombinedRoute(value, out _));
    }
}
