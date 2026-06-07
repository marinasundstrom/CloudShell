using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.DockerCompose;

public sealed class DockerComposeOrchestratorExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.orchestrators.docker-compose",
        "Docker Compose Orchestrator",
        "Runs CloudShell resource lifecycle actions through Docker Compose.",
        "0.1.0",
        ["resource-manager.orchestration", "docker-compose"],
        ["cloudshell.resource-manager"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddSingleton<DockerComposeResourceOrchestrator>();
        builder.Services.AddSingleton<IResourceOrchestrator>(
            serviceProvider => serviceProvider.GetRequiredService<DockerComposeResourceOrchestrator>());
    }
}
