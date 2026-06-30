using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

internal sealed class ContainerAppDeploymentDockerContainerOrchestrationDescriptorProvider :
    IResourceOrchestrationDescriptorProvider
{
    private const string RegistryResourceId = "docker.container:sample-registry";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool CanDescribe(Resource resource) =>
        string.Equals(
            resource.Id,
            RegistryResourceId,
            StringComparison.OrdinalIgnoreCase);

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
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
            "container-app-deployment.registry-runtime.v1",
            JsonSerializer.SerializeToElement(workload, SerializerOptions)));
    }
}
