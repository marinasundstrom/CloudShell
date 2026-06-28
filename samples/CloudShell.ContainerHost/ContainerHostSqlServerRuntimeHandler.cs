using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ContainerHost;

public interface IContainerHostSqlServerRuntimeBridge
{
    SqlServerRuntimeStatus GetStatus(ResourceModelResource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public sealed class ContainerHostSqlServerRuntimeHandler(
    IContainerHostSqlServerRuntimeBridge bridge) : ISqlServerRuntimeHandler
{
    public const string SqlServerResourceId =
        "application.sql-server:sql-server";

    public SqlServerRuntimeStatus GetStatus(ResourceModelResource resource) =>
        IsSampleSqlServer(resource)
            ? bridge.GetStatus(resource)
            : SqlServerRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSampleSqlServer(resource))
        {
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        return bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    private static bool IsSampleSqlServer(ResourceModelResource resource) =>
        string.Equals(
            resource.EffectiveResourceId,
            SqlServerResourceId,
            StringComparison.OrdinalIgnoreCase);
}
