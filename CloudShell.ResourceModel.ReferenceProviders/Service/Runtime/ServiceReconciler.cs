namespace CloudShell.ResourceModel.ReferenceProviders;

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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
