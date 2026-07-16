namespace CloudShell.ControlPlane.Providers;

public interface ILoadBalancerConfigurationApplier
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyConfigurationAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class NoopLoadBalancerConfigurationApplier :
    ILoadBalancerConfigurationApplier
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyConfigurationAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = resource.Attributes.GetString(
            LoadBalancerResourceTypeProvider.Attributes.Provider);
        var providerDetail = string.IsNullOrWhiteSpace(provider)
            ? "No load-balancer configuration applier is registered."
            : $"No load-balancer configuration applier is registered for provider '{provider}'.";

        return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            ResourceDefinitionDiagnostic.Error(
                "network.loadBalancer.configurationApplierMissing",
                $"{providerDetail} Register a load-balancer provider package before applying configuration for resource '{resource.Name}' ({resource.EffectiveResourceId}).",
                resource.EffectiveResourceId)
        ]);
    }
}
