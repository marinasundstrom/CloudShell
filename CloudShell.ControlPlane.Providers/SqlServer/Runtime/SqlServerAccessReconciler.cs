namespace CloudShell.ControlPlane.Providers;

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
