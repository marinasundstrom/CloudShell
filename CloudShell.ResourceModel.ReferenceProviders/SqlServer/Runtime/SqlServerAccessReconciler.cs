namespace CloudShell.ResourceModel.ReferenceProviders;

public interface ISqlServerAccessReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopSqlServerAccessReconciler :
    ISqlServerAccessReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
