using CloudShell.Abstractions.Extensions;
using CloudShell.Host.Components.Pages.Observability;

namespace CloudShell.Host.Shell;

public sealed class ObservabilityExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.observability",
        "Observability",
        "Telemetry-oriented views for logs, traces, metrics, and resource activity.",
        "0.1.0",
        ["observability.views", "telemetry.timeline"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddView<Components.Pages.Observability.Observability>(
            "Observability",
            "/observability",
            "pulse",
            20);
    }
}
