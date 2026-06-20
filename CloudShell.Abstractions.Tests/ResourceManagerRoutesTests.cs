using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceManagerRoutesTests
{
    [Fact]
    public void ResourceDetails_EscapesResourceId()
    {
        var route = ResourceManagerRoutes.ResourceDetails("application:orders api");

        Assert.Equal("/resources/application%3Aorders%20api", route);
    }

    [Fact]
    public void ResourceDetails_WithViewId_AddsViewSegment()
    {
        var route = ResourceManagerRoutes.ResourceDetails(
            "application:api",
            ResourcePredefinedViewIds.Endpoints);

        Assert.Equal("/resources/application%3Aapi/endpoints", route);
    }

    [Fact]
    public void ResourceOverview_TargetsStandardOverviewView()
    {
        var route = ResourceManagerRoutes.ResourceOverview("application:api");

        Assert.Equal("/resources/application%3Aapi", route);
    }

    [Fact]
    public void ResourceDetails_WithOverviewView_TargetsResourceDetailsContainer()
    {
        var route = ResourceManagerRoutes.ResourceDetails(
            "application:api",
            ResourcePredefinedViewIds.Overview);

        Assert.Equal("/resources/application%3Aapi", route);
    }

    [Fact]
    public void GetResourceViewSegment_UsesViewIdentifierByConvention()
    {
        var segment = ResourceManagerRoutes.GetResourceViewSegment(
            ResourcePredefinedViewIds.AccessControl);

        Assert.Equal("access-control", segment);
    }

    [Fact]
    public void ResourceNotFound_AddsEscapedResourceIdQuery()
    {
        var route = ResourceManagerRoutes.ResourceNotFound("application:orders api");

        Assert.Equal("/resources/not-found?resourceId=application%3Aorders%20api", route);
    }
}
