using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Providers.Docker;

public sealed class DockerProviderExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.docker",
        "Docker",
        "Adds a Docker container host resource type and maps containers as sub-resources.",
        "0.1.0",
        ["resource-type.docker.host"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.TryAddSingleton<DockerProviderOptions>();
        builder.Services.AddHostedService<DockerDiscoveryService>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IContainerHostProvider, DockerContainerHostProvider>());
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<DockerContainerResourceProvider>());
        builder.Services.AddSingleton<IResourceActionAvailabilityProvider>(
            serviceProvider => serviceProvider.GetRequiredService<DockerContainerResourceProvider>());
        builder.Services.AddSingleton<IResourceMonitoringProvider>(
            serviceProvider => serviceProvider.GetRequiredService<DockerContainerResourceProvider>());

        builder
            .AddResourceProvider<DockerContainerResourceProvider>()
            .AddLogProvider<DockerContainerResourceProvider>()
            .AddResourceType<Pages.RegisterDockerEngine>(
                DockerContainerResourceProvider.HostResourceType,
                "Container Host",
                "Register a local or remote Docker host and show its containers as sub-resources.",
                "docker",
                10,
                probeOptions: new ResourceTypeProbeOptions(
                    HealthChecks:
                    [
                        new ResourceHealthCheck(
                            "/_ping",
                            EndpointName: "host",
                            Name: "host")
                    ],
                    EnableHealthChecksByDefault: false),
                resourceClass: ResourceClass.Infrastructure)
            .AddResourceTab<Pages.DockerEngineOverview>(
                DockerContainerResourceProvider.HostResourceType,
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<Pages.DockerContainers>(
                DockerContainerResourceProvider.HostResourceType,
                new ResourceViewId(ResourceTabGroupIds.Runtime, "containers"),
                "Containers",
                20,
                groupTitle: ResourceTabGroupTitles.Runtime)
            .AddResourceTab<Pages.UpdateDockerEngine>(
                DockerContainerResourceProvider.HostResourceType,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                30,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .RegisterView<Pages.DockerContainers>(DockerProviderViews.Containers);
    }
}
