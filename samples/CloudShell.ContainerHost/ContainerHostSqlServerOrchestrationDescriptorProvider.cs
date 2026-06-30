using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ContainerHost;

internal sealed class ContainerHostSqlServerOrchestrationDescriptorProvider :
    IResourceOrchestrationDescriptorProvider
{
    private const string SqlServerResourceId = "application.sql-server:sql-server";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool CanDescribe(Resource resource) =>
        string.Equals(
            resource.Id,
            SqlServerResourceId,
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
            "container-host.sql-runtime.v1",
            JsonSerializer.SerializeToElement(workload, SerializerOptions)));
    }
}
