using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceGraphVisibility
{
    public const string HostNetworkResourceId = "network:host";
    public const string DockerHostResourceType = "docker.host";
    public const string DockerHostDefaultAttribute = "docker.host.default";

    public static bool IsImplicitDefaultResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return string.Equals(resource.Id, HostNetworkResourceId, StringComparison.OrdinalIgnoreCase) ||
            IsDefaultDockerHostResource(resource);
    }

    private static bool IsDefaultDockerHostResource(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, DockerHostResourceType, StringComparison.OrdinalIgnoreCase) &&
        resource.ResourceAttributes.TryGetValue(DockerHostDefaultAttribute, out var isDefault) &&
        bool.TryParse(isDefault, out var parsed) &&
        parsed;
}
