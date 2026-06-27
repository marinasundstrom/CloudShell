using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ContainerHost;

public interface IContainerHostGraphSqlServerRuntimeBridge
{
    SqlServerRuntimeStatus GetStatus(GraphResource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public sealed class ContainerHostGraphSqlServerRuntimeHandler(
    IContainerHostGraphSqlServerRuntimeBridge bridge) : ISqlServerRuntimeHandler
{
    public const string GraphSqlServerResourceId =
        "application.sql-server:graph-sql-server";

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
