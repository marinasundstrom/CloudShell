namespace CloudShell.ResourceModel.ReferenceProviders;

public enum SqlServerRuntimeStatus
{
    Unknown,
    Stopped,
    Running
}

public interface ISqlServerRuntimeHandler
{
    SqlServerRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public sealed class NoopSqlServerRuntimeHandler :
    ISqlServerRuntimeHandler
{
    public SqlServerRuntimeStatus GetStatus(Resource resource) =>
        SqlServerRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
