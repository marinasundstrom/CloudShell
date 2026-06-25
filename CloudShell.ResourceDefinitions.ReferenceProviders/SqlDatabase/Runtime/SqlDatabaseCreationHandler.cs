namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface ISqlDatabaseCreationHandler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EnsureCreatedAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopSqlDatabaseCreationHandler :
    ISqlDatabaseCreationHandler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EnsureCreatedAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
