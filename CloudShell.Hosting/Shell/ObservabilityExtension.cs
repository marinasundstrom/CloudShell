using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages.Logs;

namespace CloudShell.Hosting.Shell;

public sealed class ObservabilityExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.observability",
        "Observability",
        "Shared telemetry views for resources, providers, and extension-owned artifacts.",
        "0.1.0",
        ["observability.views", "logs.views", "logs.sources", "traces.views", "metrics.views"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
            .RegisterView<Components.Pages.Observability.Observability>(ObservabilityViews.Overview)
            .AddNavigationItem<Components.Pages.Observability.Observability>("Observability", "pulse", 20)
            .RegisterView<Components.Pages.Logs.Logs>(ObservabilityViews.Logs)
            .AddNavigationItem<Components.Pages.Logs.Logs>(
                ObservabilityViews.Logs,
                "Logs",
                "document",
                21,
                parentId: ObservabilityViews.Overview)
            .RegisterView<Components.Pages.Observability.DependencyGraph>(ObservabilityViews.RequestGraph)
            .AddNavigationItem<Components.Pages.Observability.DependencyGraph>(
                ObservabilityViews.RequestGraph,
                "Request graph",
                "network",
                22,
                parentId: ObservabilityViews.Overview)
            .RegisterView<Components.Pages.Observability.RequestMap>(ObservabilityViews.RequestMap)
            .AddNavigationItem<Components.Pages.Observability.RequestMap>(
                ObservabilityViews.RequestMap,
                "Request map",
                "network",
                23,
                parentId: ObservabilityViews.Overview)
            .RegisterView<Components.Pages.Observability.Traces>(ObservabilityViews.Traces)
            .AddNavigationItem<Components.Pages.Observability.Traces>(
                ObservabilityViews.Traces,
                "Traces",
                "traces",
                24,
                parentId: ObservabilityViews.Overview)
            .RegisterView<Components.Pages.Observability.Metrics>(ObservabilityViews.Metrics)
            .AddNavigationItem<Components.Pages.Observability.Metrics>(
                ObservabilityViews.Metrics,
                "Metrics",
                "metrics",
                25,
                parentId: ObservabilityViews.Overview);
    }
}
