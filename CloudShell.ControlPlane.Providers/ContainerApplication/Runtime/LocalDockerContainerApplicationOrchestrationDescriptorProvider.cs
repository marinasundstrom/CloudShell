using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Options;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalDockerContainerApplicationOrchestrationDescriptorProvider(
    IOptions<LocalDockerContainerApplicationRuntimeOptions> options) :
    IResourceOrchestrationDescriptorProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalDockerContainerApplicationRuntimeOptions options = options.Value;

    public bool CanDescribe(ResourceManagerResource resource) =>
        options.Applications.ContainsKey(resource.Id);

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
