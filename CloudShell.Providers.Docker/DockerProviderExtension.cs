using CloudShell.Abstractions.Extensions;
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
                "overview",
                "Overview",
                10)
            .AddResourceTab<Pages.UpdateDockerEngine>(
                DockerContainerResourceProvider.HostResourceType,
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true)
            .RegisterView<Pages.DockerContainers>(DockerProviderViews.Containers);
    }
}
