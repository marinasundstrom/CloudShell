namespace CloudShell.ControlPlane.Providers;

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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            SqlServerRuntimeReadiness.CreateMissingDiagnostic(resource, operationId)
        ]);
}

internal static class SqlServerRuntimeReadiness
{
    public const string DiagnosticCode = "application.sqlServer.runtimeHandlerMissing";

    public static bool IsMissing(ISqlServerRuntimeHandler? runtimeHandler) =>
        runtimeHandler is null or NoopSqlServerRuntimeHandler;

    public static string CreateMissingReason(Resource resource, ResourceOperationId operationId) =>
        $"SQL Server resource '{resource.EffectiveResourceId}' cannot execute '{operationId}' because no SQL Server runtime handler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(
        Resource resource,
        ResourceOperationId operationId) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource, operationId),
            resource.EffectiveResourceId);
}
