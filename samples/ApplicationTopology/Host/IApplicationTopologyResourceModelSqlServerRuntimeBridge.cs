using CloudShell.Abstractions.ControlPlane;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ApplicationTopologyHost;

public interface IApplicationTopologyResourceModelSqlServerRuntimeBridge
{
    SqlServerRuntimeStatus GetStatus(ResourceModelResource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}
