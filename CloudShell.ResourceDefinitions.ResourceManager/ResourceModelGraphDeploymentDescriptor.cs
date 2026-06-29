using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public interface IResourceModelGraphDeploymentDescriptor
{
    bool CanDescribeDeployment(
        ResourceManagerResource resource,
        Resource graphResource);

    ValueTask<CloudShell.Abstractions.ResourceManager.ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceModelGraphDeploymentDescriptionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceModelGraphDeploymentDescriptionContext(
    ResourceManagerResource Resource,
    Resource GraphResource,
    CloudShell.Abstractions.ResourceManager.ResourceProcedureContext ProcedureContext);
