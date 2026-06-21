using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDisplayLabelsTests
{
    [Fact]
    public void GetName_UsesResourceNameBeforeResourceId()
    {
        var resource = CreateResource(
            "application:orders-api",
            "orders-api",
            displayName: "Orders API");

        Assert.Equal("orders-api", ResourceDisplayLabels.GetName(resource));
    }

    [Fact]
    public void GetName_ParsesResourceNameFromIdWhenResourceIsUnavailable()
    {
        Assert.Equal("orders-api", ResourceDisplayLabels.GetName("application:orders-api"));
    }

    [Fact]
    public void GetQualifiedLabel_IncludesNameWhenDisplayNameDiffers()
    {
        var resource = CreateResource(
            "configuration:application-topology",
            "application-topology",
            displayName: "Settings");

        Assert.Equal("Settings (application-topology)", ResourceDisplayLabels.GetQualifiedLabel(resource));
    }

    private static Resource CreateResource(
        string id,
        string name,
        string? displayName = null) =>
        new(
            id,
            name,
            "Test",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            DisplayName: displayName);
}
