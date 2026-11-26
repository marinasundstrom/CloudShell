using CloudShell.Abstractions.ResourceManager;
using CoreShell;

namespace CloudShell.Hosting.ResourceManager;

/// <summary>Preview UI context shared by operations panes and composed dashboard sections.</summary>
public sealed record OperationsWorkspaceContext(
    IReadOnlyList<Resource> Resources,
    Resource? Resource,
    string? ResourceId,
    bool IsLoading,
    string? Error,
    DateTimeOffset? UpdatedAt,
    Func<Resource, string> GetLabel)
{
    public IReadOnlyDictionary<string, ResourceHealthSummary> Health { get; init; } = new Dictionary<string, ResourceHealthSummary>();
    public bool IsSystem => string.IsNullOrEmpty(ResourceId);
    public string DashboardHref(string? resourceId = null) => OperationsNavigation.Dashboard(resourceId);
}

/// <summary>Preview extension points. Contribute sections using normal CoreShell modules.</summary>
public static class OperationsWorkspaceIds
{
    public static readonly CoreShellPageId Page = CoreShellPageId.Create("cloudshell.operations");
    public static readonly CoreShellSectionOutletId Widgets = CoreShellSectionOutletId.Create(Page, "widgets");
    public static readonly CoreShellSectionOutletId Panes = CoreShellSectionOutletId.Create(Page, "panes");
    public const string Scope = "operations.scope"; // system, resource, or both (default)
    public const string ResourceType = "operations.resource-type";
    public const string DefaultWidget = "operations.default";
}

public static class OperationsNavigation
{
    public static string Dashboard(string? resourceId) => string.IsNullOrWhiteSpace(resourceId)
        ? "/resources?operation=dashboard" : $"/resources/{Uri.EscapeDataString(resourceId)}";

    public static bool IsOperational(ResourceTabContribution tab) => tab.IsOperational || IsOperational(tab.Id);

    public static bool IsOperational(ResourceViewId id) =>
        id == ResourcePredefinedViewIds.Logs || id == ResourcePredefinedViewIds.Traces ||
        id == ResourcePredefinedViewIds.Metrics || id == ResourcePredefinedViewIds.Monitoring ||
        id == ResourcePredefinedViewIds.Health || id == ResourcePredefinedViewIds.Activity ||
        id == ResourcePredefinedViewIds.Usage;

    public static string Pane(string? resourceId, string pane) => pane switch
    {
        "dashboard" => Dashboard(resourceId),
        "logs" => resourceId is null ? "/resources?operation=logs" : ResourceManagerRoutes.ResourceDetails(resourceId, ResourcePredefinedViewIds.Logs),
        "traces" => resourceId is null ? "/resources?operation=traces" : ResourceManagerRoutes.ResourceDetails(resourceId, ResourcePredefinedViewIds.Traces),
        "metrics" => resourceId is null ? "/resources?operation=metrics" : ResourceManagerRoutes.ResourceDetails(resourceId, ResourcePredefinedViewIds.Metrics),
        _ => resourceId is null ? Dashboard(null) : $"/resources/{Uri.EscapeDataString(resourceId)}/{Uri.EscapeDataString(pane)}"
    };

    public static (string? ResourceId, string Pane) Parse(string relativeUri)
    {
        var path = relativeUri.Split('?', '#')[0].Trim('/').Split('/');
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("http://workspace/" + relativeUri.TrimStart('/')).Query);
        if (path.Length >= 2 && path[0] == "resources")
            return (Uri.UnescapeDataString(path[1]), path.Length > 2 ? Uri.UnescapeDataString(path[2]) : "dashboard");
        return (query.TryGetValue("resourceId", out var id) && !string.IsNullOrWhiteSpace(id) ? id.ToString() : null,
            path[0] == "logs" ? "logs" : path[0] is "telemetry" or "observability" && path.Length > 1 ? path[1] : query.TryGetValue("operation", out var operation) ? operation.ToString() : "resources");
    }
}
