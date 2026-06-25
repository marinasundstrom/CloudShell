namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface IDnsZoneNameMappingReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopDnsZoneNameMappingReconciler :
    IDnsZoneNameMappingReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
