namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface ILoadBalancerConfigurationApplier
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyConfigurationAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopLoadBalancerConfigurationApplier :
    ILoadBalancerConfigurationApplier
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyConfigurationAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
