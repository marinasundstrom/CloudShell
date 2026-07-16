namespace CloudShell.ControlPlane.Providers;

public interface IServiceReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopServiceReconciler :
    IServiceReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            ServiceReconcilerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class ServiceReconcilerReadiness
{
    public const string DiagnosticCode = "service.reconcilerMissing";

    public static bool IsMissing(IServiceReconciler? reconciler) =>
        reconciler is null or NoopServiceReconciler;

    public static string CreateMissingReason(Resource resource) =>
        $"Service resource '{resource.EffectiveResourceId}' cannot reconcile endpoint or service materialization because no service reconciler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
