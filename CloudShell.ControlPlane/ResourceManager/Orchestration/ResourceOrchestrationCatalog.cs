using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public sealed class ResourceOrchestrationCatalog(
    IResourceManagerStore resourceManager,
    IResourceRegistrationStore registrations,
    IEnumerable<IResourceOrchestrationDescriptorProvider> descriptorProviders) : IResourceOrchestrationCatalog
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyList<IResourceOrchestrationDescriptorProvider> descriptorProviders =
        descriptorProviders.ToArray();

    public async Task<ResourceOrchestrationCatalogSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var resources = resourceManager.GetResources();
        var workloads = new Dictionary<string, ResourceWorkloadConfiguration>(StringComparer.OrdinalIgnoreCase);
        var containerHosts = new Dictionary<string, ContainerHostDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = await TryDescribeAsync(resource, cancellationToken);
            if (descriptor is null)
            {
                continue;
            }

            var workload = TryDeserialize<ResourceWorkloadConfiguration>(descriptor.Configuration);
            if (workload is not null)
            {
                workloads[resource.Id] = workload;
            }

            if (descriptor.ResourceType.Equals(
                    ContainerHostResourceTypes.ContainerHost,
                    StringComparison.OrdinalIgnoreCase))
            {
                var host = TryDeserialize<ContainerHostDescriptor>(descriptor.Configuration);
                if (host is not null)
                {
                    containerHosts[resource.Id] = host;
                }
            }
        }

        return new ResourceOrchestrationCatalogSnapshot(resources, workloads, containerHosts);
    }

    private async Task<ResourceOrchestrationDescriptor?> TryDescribeAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        var provider = descriptorProviders.FirstOrDefault(provider => provider.CanDescribe(resource));
        if (provider is null)
        {
            return null;
        }

        try
        {
            return await provider.DescribeAsync(
                resource,
                new ResourceOrchestrationDescriptorContext(
                    registrations.GetRegistration(resource.Id),
                    resourceManager.GetGroupForResource(resource.Id),
                    resourceManager),
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static T? TryDeserialize<T>(JsonElement configuration)
    {
        try
        {
            return configuration.Deserialize<T>(SerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
