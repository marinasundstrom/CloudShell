namespace CloudShell.ResourceModel.ReferenceProviders;

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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
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
