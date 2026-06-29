namespace CloudShell.ResourceModel.ReferenceProviders;

public interface IDnsZoneNameMappingReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class NoopDnsZoneNameMappingReconciler :
    IDnsZoneNameMappingReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
