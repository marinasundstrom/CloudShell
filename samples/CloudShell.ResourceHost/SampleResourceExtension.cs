using CloudShell.Abstractions.Extensions;
using CloudShell.ResourceHost.Pages;
using CloudShell.Hosting.ResourceManager;
using CoreShell;
using CoreShell.Blazor;

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
        builder.AddCoreShellModule(CoreShellModuleId.Create("sample.operations"), module =>
        {
            module.ExtendSections(OperationsWorkspaceIds.Page, OperationsWorkspaceIds.Widgets)
                .AddSection<SampleOperationsSummary>(CoreShellSectionId.Create(OperationsWorkspaceIds.Widgets, "sample-summary"),
                    "Sample service", 25, new Dictionary<string, string>
                    {
                        [OperationsWorkspaceIds.Scope] = "resource",
                        [OperationsWorkspaceIds.ResourceType] = "sample-service",
                        [OperationsWorkspaceIds.DefaultWidget] = "true"
                    });
            module.ExtendSections(OperationsWorkspaceIds.Page, OperationsWorkspaceIds.Panes)
                .AddSection<SampleOperationsSummary>(CoreShellSectionId.Create(OperationsWorkspaceIds.Panes, "sample-details"),
                    "Service details", 20, new Dictionary<string, string>
                    {
                        [OperationsWorkspaceIds.Scope] = "resource",
                        [OperationsWorkspaceIds.ResourceType] = "sample-service"
                    });
        });

        builder
            .AddResourceType<RegisterSampleResource>(
                "sample-service",
                "Sample service",
                "A static sample resource surfaced by a CloudShell resource provider.",
                "server",
                5);
    }
}
