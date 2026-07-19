using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal sealed record ResourceTabComponentContext(
    Resource Resource,
    ResourceTypeContribution? ResourceType,
    ResourceTabContribution Tab,
    ResourceTabResolution TabResolution,
    string? TraceId = null,
    string? SpanId = null,
    string? LogSourceId = null,
    string? LogView = null,
    string? LogSourceIds = null,
    IReadOnlyList<LogSource>? LogSources = null,
    string? ScopeResourceId = null);

internal static class ResourceTabComponentParameterResolver
{
    public static Dictionary<string, object?> Resolve(ResourceTabComponentContext context)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["ResourceId"] = context.Resource.Id
        };

        if (!context.TabResolution.IsGenerated(context.Tab.Id))
        {
            return parameters;
        }

        if (context.ResourceType is not null &&
            context.TabResolution.AcceptsResourceTypeContext(context.Tab.Id))
        {
            parameters["ResourceType"] = context.ResourceType;
        }

        if (context.Tab.Id == ResourcePredefinedViewIds.Activity)
        {
            parameters["TraceId"] = context.TraceId;
            parameters["SpanId"] = context.SpanId;
        }
        else if (context.Tab.Id == ResourcePredefinedViewIds.Logs)
        {
            parameters["LogSourceId"] = context.LogSourceId;
            parameters["TraceId"] = context.TraceId;
            parameters["View"] = context.LogView;
            parameters["SourceIds"] = context.LogSourceIds;
            parameters["LogSources"] = context.LogSources ?? [];
        }
        else if (context.Tab.Id == ResourcePredefinedViewIds.Traces)
        {
            parameters["TraceId"] = context.TraceId;
            parameters["ScopeResourceId"] = context.ScopeResourceId;
        }
        else if (context.Tab.Id == ResourcePredefinedViewIds.Metrics)
        {
            parameters["ScopeResourceId"] = context.ScopeResourceId;
        }

        return parameters;
    }
}
