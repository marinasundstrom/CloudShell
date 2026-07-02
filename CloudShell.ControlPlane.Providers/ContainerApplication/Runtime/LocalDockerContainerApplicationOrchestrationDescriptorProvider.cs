using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalDockerContainerApplicationOrchestrationDescriptorProvider :
    IResourceOrchestrationDescriptorProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool CanDescribe(ResourceManagerResource resource) =>
        string.Equals(
            resource.EffectiveTypeId,
            ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        ResourceManagerResource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        var workload = new ResourceWorkloadConfiguration(
            ResourceWorkloadKind.LocalExecutable,
            resource.Name,
            Lifetime: ResourceLifetime.ControlPlaneScoped);

        return Task.FromResult(new ResourceOrchestrationDescriptor(
            resource.Id,
            resource.EffectiveTypeId,
            resource.DependsOn,
            [],
            resource.Endpoints,
            "local-docker-container-application.runtime-workload.v1",
            JsonSerializer.SerializeToElement(workload, SerializerOptions)));
    }
}
