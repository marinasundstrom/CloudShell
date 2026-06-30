using CloudShell.Abstractions.ControlPlane;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ApplicationTopologyHost;

public interface IApplicationTopologyResourceModelSqlServerRuntimeBridge
{
    SqlServerRuntimeStatus GetStatus(ResourceModelResource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}
