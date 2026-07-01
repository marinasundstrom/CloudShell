using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerHostOrchestrationDescriptorProvider : IResourceOrchestrationDescriptorProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool CanDescribe(ResourceManagerResource resource) =>
        resource.EffectiveTypeId == ContainerHostResourceTypeProvider.ResourceTypeId;

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        ResourceManagerResource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var descriptor = new ContainerHostDescriptor(
            resource.Id,
            resource.EffectiveDisplayName,
            ResolveKind(resource),
            ResolveEndpoint(resource),
            IsDefault: ResolveDefault(resource),
            Registry: ResolveRegistry(resource),
            Capabilities: ResolveCapabilities(resource));

        return Task.FromResult(new ResourceOrchestrationDescriptor(
            resource.Id,
            ContainerHostResourceTypes.ContainerHost,
            resource.DependsOn,
            [],
            resource.Endpoints,
            "1.0",
            JsonSerializer.SerializeToElement(descriptor, SerializerOptions)));
    }

    private static ContainerHostKind ResolveKind(ResourceManagerResource resource)
    {
        var hostKind = GetAttribute(resource, ContainerHostResourceTypeProvider.Attributes.HostKind);
        return Enum.TryParse<ContainerHostKind>(hostKind, ignoreCase: true, out var kind)
            ? kind
            : ContainerHostKind.Custom;
    }

    private static string ResolveEndpoint(ResourceManagerResource resource) =>
        NormalizeOptional(GetAttribute(resource, ContainerHostResourceTypeProvider.Attributes.Endpoint))
        ?? "unix:///var/run/docker.sock";

    private static bool ResolveDefault(ResourceManagerResource resource) =>
        bool.TryParse(
            GetAttribute(resource, ContainerHostResourceTypeProvider.Attributes.IsDefault),
            out var isDefault) &&
        isDefault;

    private static string ResolveRegistry(ResourceManagerResource resource) =>
        NormalizeOptional(GetAttribute(resource, ContainerHostResourceTypeProvider.Attributes.Registry))
        ?? ContainerRegistryDefaults.Default;

    private static IReadOnlyList<string> ResolveCapabilities(ResourceManagerResource resource)
    {
        var capabilities = new List<string>();
        AddIfSupported(
            resource,
            ContainerHostResourceTypeProvider.Capabilities.ContainerImage,
            ContainerHostCapabilityIds.ContainerImage,
            capabilities);
        AddIfSupported(
            resource,
            ContainerHostResourceTypeProvider.Capabilities.ContainerBuild,
            ContainerHostCapabilityIds.ContainerBuild,
            capabilities);
        AddIfSupported(
            resource,
            ContainerHostResourceTypeProvider.Capabilities.StorageMountFileSystem,
            ContainerHostCapabilityIds.StorageMountFileSystem,
            capabilities);

        return capabilities;
    }

    private static void AddIfSupported(
        ResourceManagerResource resource,
        ResourceCapabilityId resourceCapability,
        string hostCapability,
        List<string> capabilities)
    {
        if (resource.ResourceCapabilities.Any(capability =>
                string.Equals(capability.Id, resourceCapability, StringComparison.OrdinalIgnoreCase)))
        {
            capabilities.Add(hostCapability);
        }
    }

    private static string? GetAttribute(
        ResourceManagerResource resource,
        ResourceAttributeId attributeId) =>
        resource.ResourceAttributes.GetValueOrDefault(attributeId);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
