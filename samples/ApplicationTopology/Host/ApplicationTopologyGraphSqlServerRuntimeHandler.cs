using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ApplicationTopologyHost;

public sealed class ApplicationTopologyGraphSqlServerRuntimeHandler(
    IApplicationTopologyGraphSqlServerRuntimeBridge bridge) : ISqlServerRuntimeHandler
{
    public const string GraphSqlServerResourceId =
        "application.sql-server:application-topology-sql-server";

    public SqlServerRuntimeStatus GetStatus(GraphResource resource) =>
        IsGraphSqlServer(resource)
            ? bridge.GetStatus(resource)
            : SqlServerRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphSqlServer(resource))
        {
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        return bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    private static bool IsGraphSqlServer(GraphResource resource) =>
        string.Equals(
            resource.EffectiveResourceId,
            GraphSqlServerResourceId,
            StringComparison.OrdinalIgnoreCase);
}
