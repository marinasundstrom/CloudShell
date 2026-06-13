using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Docker;

internal sealed class DockerContainerEngineProvider(
    DockerProviderOptions options) : IContainerEngineProvider, IContainerHostProvider
{
    public ContainerEngineResourceDefinition GetContainerEngine() =>
        new(
            "docker",
            "Docker",
            ContainerEngineKind.Docker,
            options.ResolveEndpoint().ToString(),
            IsDefault: true,
            Registry: NormalizeRegistry(options.Registry));

    public ContainerHostDescriptor GetDefaultHost() =>
        GetContainerEngine().ToContainerHostDescriptor();

    private static string NormalizeRegistry(string? registry) =>
        string.IsNullOrWhiteSpace(registry)
            ? DockerProviderOptions.DefaultRegistry
            : registry.Trim();
}
