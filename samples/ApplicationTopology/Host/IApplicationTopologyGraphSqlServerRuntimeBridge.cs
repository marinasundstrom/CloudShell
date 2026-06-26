using CloudShell.Abstractions.ControlPlane;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ApplicationTopologyHost;

public interface IApplicationTopologyGraphSqlServerRuntimeBridge
{
    SqlServerRuntimeStatus GetStatus(GraphResource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}
