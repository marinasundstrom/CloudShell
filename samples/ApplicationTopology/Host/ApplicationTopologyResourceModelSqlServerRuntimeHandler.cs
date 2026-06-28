using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ApplicationTopologyHost;

public sealed class ApplicationTopologyResourceModelSqlServerRuntimeHandler(
    IApplicationTopologyResourceModelSqlServerRuntimeBridge bridge) : ISqlServerRuntimeHandler
{
    public const string ResourceModelSqlServerResourceId =
        "application.sql-server:application-topology-sql-server";

    public SqlServerRuntimeStatus GetStatus(ResourceModelResource resource) =>
        IsResourceModelSqlServer(resource)
            ? bridge.GetStatus(resource)
            : SqlServerRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsResourceModelSqlServer(resource))
        {
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        return bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    private static bool IsResourceModelSqlServer(ResourceModelResource resource) =>
        string.Equals(
            resource.EffectiveResourceId,
            ResourceModelSqlServerResourceId,
            StringComparison.OrdinalIgnoreCase);
}
