namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface ILocalHostNetworkEndpointMappingReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class NoopLocalHostNetworkEndpointMappingReconciler :
    ILocalHostNetworkEndpointMappingReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
