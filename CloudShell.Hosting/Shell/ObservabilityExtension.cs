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
        ["observability.views", "logs.views", "logs.sources", "traces.views"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
            .RegisterView<Components.Pages.Observability.Observability>(ObservabilityViews.Overview)
            .AddNavigationItem<Components.Pages.Observability.Observability>("Observability", "pulse", 20)
            .RegisterView<Components.Pages.Logs.Logs>(ObservabilityViews.Logs)
            .AddNavigationItem<Components.Pages.Logs.Logs>("Logs", "document", 21)
            .RegisterView<Components.Pages.Observability.Traces>(ObservabilityViews.Traces)
            .AddNavigationItem<Components.Pages.Observability.Traces>("Traces", "traces", 22);
    }
}
