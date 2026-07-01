using CloudShell.Abstractions.Extensions;
using CloudShell.ResourceHost.Pages;

namespace CloudShell.ResourceHost;

public sealed class SampleResourceControlPlaneExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "sample.resources",
        "Sample Resources",
        "A resource provider extension hosted with the CloudShell control plane.",
        "0.1.0",
        ["sample.resources"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
            .AddResourceProvider<SampleResourceProvider>();
    }
}

public sealed class SampleResourceManagerUiExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "sample.resources.ui",
        "Sample Resources UI",
        "Resource Manager UI integration for the sample resource provider.",
        "0.1.0",
        ["sample.resources.ui"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
            .AddResourceType<RegisterSampleResource>(
                "sample-service",
                "Sample service",
                "A static sample resource surfaced by a CloudShell resource provider.",
                "server",
                5);
    }
}
