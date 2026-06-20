using CloudShell.Abstractions.Authorization;
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
            .AddNavigationItem<Components.Pages.Observability.Observability>(
                "Observability",
                "pulse",
                20,
                requiredPermissions: ObservabilityAuthorization.AnyReadPermissions)
            .RegisterView<Components.Pages.Logs.Logs>(ObservabilityViews.Logs)
            .AddNavigationItem<Components.Pages.Logs.Logs>(
                ObservabilityViews.Logs,
                "Logs",
                "document",
                21,
                parentId: ObservabilityViews.Overview,
                requiredPermissions: ObservabilityAuthorization.LogsReadPermissions)
            .RegisterView<Components.Pages.Observability.DependencyGraph>(ObservabilityViews.Dependencies)
            .AddNavigationItem<Components.Pages.Observability.DependencyGraph>(
                ObservabilityViews.Dependencies,
                "Dependencies",
                "dependencies",
                22,
                parentId: ObservabilityViews.Overview,
                requiredPermissions: ObservabilityAuthorization.TracesReadPermissions)
            .RegisterView<Components.Pages.Observability.ServiceMap>(ObservabilityViews.ServiceMap)
            .AddNavigationItem<Components.Pages.Observability.ServiceMap>(
                ObservabilityViews.ServiceMap,
                "Service map",
                "service-map",
                23,
                parentId: ObservabilityViews.Overview,
                requiredPermissions: ObservabilityAuthorization.TracesReadPermissions)
            .RegisterView<Components.Pages.Observability.Traces>(ObservabilityViews.Traces)
            .AddNavigationItem<Components.Pages.Observability.Traces>(
                ObservabilityViews.Traces,
                "Traces",
                "traces",
                24,
                parentId: ObservabilityViews.Overview,
                requiredPermissions: ObservabilityAuthorization.TracesReadPermissions)
            .RegisterView<Components.Pages.Observability.Metrics>(ObservabilityViews.Metrics)
            .AddNavigationItem<Components.Pages.Observability.Metrics>(
                ObservabilityViews.Metrics,
                "Metrics",
                "metrics",
                25,
                parentId: ObservabilityViews.Overview,
                requiredPermissions: ObservabilityAuthorization.MetricsReadPermissions);
    }
}
