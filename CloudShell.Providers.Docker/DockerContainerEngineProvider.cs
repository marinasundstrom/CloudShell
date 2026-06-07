using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Docker;

internal sealed class DockerContainerEngineProvider(
    DockerProviderOptions options) : IContainerEngineProvider
{
    public ContainerEngineResourceDefinition GetContainerEngine() =>
        new(
            "docker",
            "Docker",
            ContainerEngineKind.Docker,
            options.ResolveEndpoint().ToString(),
            IsDefault: true);
}
