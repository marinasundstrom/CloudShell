using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ApplicationTopologyHost;

internal sealed class ApplicationTopologyResourceModelSqlServerOrchestrationDescriptorProvider :
    IResourceOrchestrationDescriptorProvider
{
    private const string ResourceModelSqlServerResourceId =
        "application.sql-server:application-topology-sql-server";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool CanDescribe(Resource resource) =>
        string.Equals(
            resource.Id,
            ResourceModelSqlServerResourceId,
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
            "application-topology.resource-model-sql-runtime.v1",
            JsonSerializer.SerializeToElement(workload, SerializerOptions)));
    }
}
