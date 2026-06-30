using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceGraphVisibility
{
    public const string HostNetworkResourceId = "network:host";
    public const string ContainerHostResourceType = "cloudshell.container-host";
    public const string ContainerHostDefaultAttribute = "container.host.default";
    public const string DockerHostResourceType = "docker.host";
    public const string DockerHostDefaultAttribute = "docker.host.default";

    public static bool IsImplicitDefaultResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.IsProjectedResource &&
            (string.Equals(resource.Id, HostNetworkResourceId, StringComparison.OrdinalIgnoreCase) ||
                IsDefaultContainerHostResource(resource));
    }

    private static bool IsDefaultContainerHostResource(Resource resource) =>
        IsContainerHostResourceType(resource.EffectiveTypeId) &&
        TryGetDefaultContainerHostValue(resource, out var isDefault) &&
        bool.TryParse(isDefault, out var parsed) &&
        parsed;

    private static bool IsContainerHostResourceType(string typeId) =>
        string.Equals(typeId, ContainerHostResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(typeId, DockerHostResourceType, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetDefaultContainerHostValue(Resource resource, out string value) =>
        resource.ResourceAttributes.TryGetValue(ContainerHostDefaultAttribute, out value!) ||
        resource.ResourceAttributes.TryGetValue(DockerHostDefaultAttribute, out value!);
}
