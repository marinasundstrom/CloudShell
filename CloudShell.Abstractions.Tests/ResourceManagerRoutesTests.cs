using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceManagerRoutesTests
{
    [Fact]
    public void ResourceDetails_EscapesResourceId()
    {
        var route = ResourceManagerRoutes.ResourceDetails("application:orders api");

        Assert.Equal("/resources/application%3Aorders%20api/details", route);
    }

    [Fact]
    public void ResourceDetails_WithViewId_AddsEscapedTabQuery()
    {
        var route = ResourceManagerRoutes.ResourceDetails(
            "application:api",
            ResourcePredefinedViewIds.Endpoints);

        Assert.Equal("/resources/application%3Aapi/details?tab=networking%3Aendpoints", route);
    }

    [Fact]
    public void ResourceOverview_TargetsStandardOverviewView()
    {
        var route = ResourceManagerRoutes.ResourceOverview("application:api");

        Assert.Equal("/resources/application%3Aapi/details?tab=general%3Aoverview", route);
    }

    [Fact]
    public void ResourceNotFound_AddsEscapedResourceIdQuery()
    {
        var route = ResourceManagerRoutes.ResourceNotFound("application:orders api");

        Assert.Equal("/resources/not-found?resourceId=application%3Aorders%20api", route);
    }
}
