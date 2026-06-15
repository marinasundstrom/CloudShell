using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Docker;

internal sealed class DockerContainerHostProvider(
    DockerProviderOptions options) : IContainerHostProvider
{
    public ContainerHostDescriptor GetDefaultHost() =>
        new(
            "docker",
            "Docker",
            ContainerHostKind.Docker,
            options.ResolveEndpoint().ToString(),
            IsDefault: true,
            Registry: NormalizeRegistry(options.Registry),
            Capabilities:
            [
                ContainerHostCapabilityIds.ContainerImage,
                ContainerHostCapabilityIds.ContainerBuild,
                ContainerHostCapabilityIds.StorageMountFileSystem
            ]);

    private static string NormalizeRegistry(string? registry) =>
        string.IsNullOrWhiteSpace(registry)
            ? DockerProviderOptions.DefaultRegistry
            : registry.Trim();
}
