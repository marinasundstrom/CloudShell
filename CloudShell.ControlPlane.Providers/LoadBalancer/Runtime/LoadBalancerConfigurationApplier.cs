namespace CloudShell.ControlPlane.Providers;

public interface ILoadBalancerConfigurationApplier
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyConfigurationAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default);
}

internal static class LoadBalancerConfigurationApplierReadiness
{
    public const string MissingConfigurationApplierDiagnosticCode =
        "network.loadBalancer.configurationApplierMissing";

    public static bool IsMissing(
        ILoadBalancerConfigurationApplier? configurationApplier) =>
        configurationApplier is null or NoopLoadBalancerConfigurationApplier;

    public static string CreateMissingConfigurationApplierReason(
        Resource resource)
    {
        var provider = resource.Attributes.GetString(
            LoadBalancerResourceTypeProvider.Attributes.Provider);
        var providerDetail = string.IsNullOrWhiteSpace(provider)
            ? "No load-balancer configuration applier is registered."
            : $"No load-balancer configuration applier is registered for provider '{provider}'.";

        return $"{providerDetail} Register a load-balancer provider package before applying configuration for resource '{resource.Name}' ({resource.EffectiveResourceId}).";
    }

    public static ResourceDefinitionDiagnostic CreateMissingConfigurationApplierDiagnostic(
        Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            MissingConfigurationApplierDiagnosticCode,
            CreateMissingConfigurationApplierReason(resource),
            resource.EffectiveResourceId);
}

public sealed class NoopLoadBalancerConfigurationApplier :
    ILoadBalancerConfigurationApplier
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyConfigurationAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default)
    =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            LoadBalancerConfigurationApplierReadiness
                .CreateMissingConfigurationApplierDiagnostic(resource)
        ]);
}
