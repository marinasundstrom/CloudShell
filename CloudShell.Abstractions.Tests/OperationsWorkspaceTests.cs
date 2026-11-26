using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class OperationsWorkspaceTests
{
    [Theory]
    [InlineData("resources", null, "resources")]
    [InlineData("resources?operation=dashboard", null, "dashboard")]
    [InlineData("resources?operation=logs&resourceId=app%2Fapi", "app/api", "logs")]
    [InlineData("resources/app%2Fapi", "app/api", "dashboard")]
    [InlineData("resources/app%2Fapi/dashboard", "app/api", "dashboard")]
    [InlineData("resources/app%2Fapi/traces?traceId=abc", "app/api", "traces")]
    public void Parse_DistinguishesInventorySystemAndResourceScope(string uri, string? id, string pane)
    {
        var result = OperationsNavigation.Parse(uri);
        Assert.Equal(id, result.ResourceId);
        Assert.Equal(pane, result.Pane);
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("logs")]
    [InlineData("traces")]
    public void ResourceLinksRoundTripOpaqueIds(string pane)
    {
        var href = OperationsNavigation.Pane("apps/team:api", pane);
        var parsed = OperationsNavigation.Parse(href);
        Assert.Equal("apps/team:api", parsed.ResourceId);
        Assert.Equal(pane, parsed.Pane);
    }

    [Fact]
    public void SystemOperationalLinksStayInsideResources()
    {
        Assert.StartsWith("/resources?", OperationsNavigation.Pane(null, "logs"));
        Assert.StartsWith("/resources?", OperationsNavigation.Pane(null, "traces"));
    }

    [Fact]
    public void DashboardKeysSeparateEnvironmentAndScopes()
    {
        var keys = new[] { OperationsDashboardLayout.SettingKey("a", null), OperationsDashboardLayout.SettingKey("a", "system"), OperationsDashboardLayout.SettingKey("b", null), OperationsDashboardLayout.SettingKey("a", "other") };
        Assert.Equal(4, keys.Distinct().Count());
    }

    [Fact]
    public void LayoutRetainsMissingWidgetsAndExplicitEmptyDashboard()
    {
        Assert.Equal(new[] { "missing-extension" }, OperationsDashboardLayout.Parse("{\"Version\":1,\"Widgets\":[\"missing-extension\"]}", ["default"]).Widgets);
        Assert.Empty(OperationsDashboardLayout.Parse("{\"Version\":1,\"Widgets\":[]}", ["default"]).Widgets);
        Assert.Equal(new[] { "default" }, OperationsDashboardLayout.Parse(null, ["default"]).Widgets);
    }

    [Theory]
    [InlineData("{\"Version\":2,\"Widgets\":[]}")]
    [InlineData("{\"Version\":1,\"Widgets\":[\"duplicate\",\"duplicate\"]}")]
    [InlineData("{\"Version\":1,\"Widgets\":null}")]
    [InlineData("not json")]
    public void LayoutRejectsInvalidOrUnsupportedState(string json) => Assert.Throws<JsonException>(() => OperationsDashboardLayout.Parse(json, []));

    [Fact]
    public void ConfigurationAndCustomOperationsAreExplicitlySeparated()
    {
        var custom = new ResourceTabContribution(new ResourceViewId(ResourceTabGroupIds.General, "custom"), "Custom", 10, typeof(object), IsOperational: true);
        Assert.True(OperationsNavigation.IsOperational(custom));
        Assert.False(OperationsNavigation.IsOperational(custom with { IsOperational = false }));
        Assert.True(OperationsNavigation.IsOperational(ResourcePredefinedViewIds.Logs));
        Assert.False(OperationsNavigation.IsOperational(ResourcePredefinedViewIds.Configuration));
        Assert.False(OperationsNavigation.IsOperational(ResourcePredefinedViewIds.Environment));
    }

    [Fact]
    public void ResourceOnlyContributionsDoNotLeakIntoSystemDashboard()
    {
        var system = new OperationsWorkspaceContext([], null, null, false, null, null, r => r.Name);
        Assert.False(OperationsContributions.Applies(new Dictionary<string,string> { [OperationsWorkspaceIds.Scope] = "resource" }, system));
        Assert.False(OperationsContributions.Applies(new Dictionary<string,string> { [OperationsWorkspaceIds.ResourceType] = "sql.server" }, system));
        Assert.True(OperationsContributions.Applies(new Dictionary<string,string> { [OperationsWorkspaceIds.Scope] = "system" }, system));
    }
}
