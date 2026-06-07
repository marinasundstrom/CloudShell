using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages.Logs;

namespace CloudShell.Hosting.Shell;

public sealed class ObservabilityExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.observability",
        "Logs",
        "Shared log views for resources, providers, and extension-owned artifacts.",
        "0.1.0",
        ["logs.views", "logs.sources"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddView<Components.Pages.Logs.Logs>(
            "Logs",
            "/logs",
            "document",
            20);
    }
}
