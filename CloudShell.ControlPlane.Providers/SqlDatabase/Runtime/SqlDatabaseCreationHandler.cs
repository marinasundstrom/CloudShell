namespace CloudShell.ControlPlane.Providers;

public interface ISqlDatabaseCreationHandler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EnsureCreatedAsync(
        SqlDatabaseCreationContext context,
        CancellationToken cancellationToken = default);
}

public interface ISqlDatabaseServerResolver
{
    ValueTask<Resource?> ResolveServerAsync(
        Resource database,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record SqlDatabaseCreationContext(
    Resource Database,
    Resource Server);

public sealed class NoopSqlDatabaseCreationHandler :
    ISqlDatabaseCreationHandler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EnsureCreatedAsync(
        SqlDatabaseCreationContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            SqlDatabaseCreationReadiness.CreateMissingDiagnostic(context.Database)
        ]);
}

internal static class SqlDatabaseCreationReadiness
{
    public const string DiagnosticCode = "application.sqlDatabase.creationHandlerMissing";

    public static bool IsMissing(ISqlDatabaseCreationHandler? creationHandler) =>
        creationHandler is null or NoopSqlDatabaseCreationHandler;

    public static string CreateMissingReason(Resource resource) =>
        $"SQL database resource '{resource.EffectiveResourceId}' cannot be ensured because no SQL database creation handler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}

public sealed class ContextSqlDatabaseServerResolver :
    ISqlDatabaseServerResolver
{
    public ValueTask<Resource?> ResolveServerAsync(
        Resource database,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult(
            SqlDatabaseResourceTypeProvider.TryGetServerDependencyResourceId(
                database.State,
                out var serverResourceId)
                    ? context.FindResource(serverResourceId)
                    : null);
    }
}
