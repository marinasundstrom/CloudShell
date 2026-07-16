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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            SqlServerAccessReconcilerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class SqlServerAccessReconcilerReadiness
{
    public const string DiagnosticCode = "application.sqlServer.accessReconcilerMissing";

    public static bool IsMissing(ISqlServerAccessReconciler? accessReconciler) =>
        accessReconciler is null or NoopSqlServerAccessReconciler;

    public static string CreateMissingReason(Resource resource) =>
        $"SQL Server '{resource.EffectiveResourceId}' cannot reconcile database access because no SQL Server access reconciler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
