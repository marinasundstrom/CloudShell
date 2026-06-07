using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.DockerCompose;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Providers.Docker;

public sealed class DockerProviderExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.docker",
        "Docker",
        "Adds a Docker Engine resource type and maps local containers as sub-resources.",
        "0.1.0",
        ["resource-type.docker.engine", "resource-manager.orchestration", "docker-compose"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.TryAddSingleton<DockerProviderOptions>();
        builder.Services.AddHostedService<DockerDiscoveryService>();
        builder.Services.TryAddSingleton<DockerComposeOrchestratorOptions>();
        builder.Services.TryAddSingleton<DockerComposeResourceOrchestrator>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOrchestrator, DockerComposeResourceOrchestrator>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IContainerEngineProvider, DockerContainerEngineProvider>());
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<DockerContainerResourceProvider>());

        builder
            .AddResourceProvider<DockerContainerResourceProvider>()
            .AddLogProvider<DockerContainerResourceProvider>()
            .AddResourceType<Pages.RegisterDockerEngine>(
                "docker.engine",
                "Docker Engine",
                "Register a local Docker Engine and show its containers as sub-resources.",
                "docker",
                10)
            .AddResourceTab<Pages.DockerEngineOverview>(
                "docker.engine",
                "overview",
                "Overview",
                10)
            .AddResourceTab<Pages.UpdateDockerEngine>(
                "docker.engine",
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true)
            .RegisterView<Pages.DockerContainers>(DockerProviderViews.Containers);
    }
}
