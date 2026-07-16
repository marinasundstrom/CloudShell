namespace CloudShell.ControlPlane.Providers;

public interface IDnsZoneNameMappingReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default);
}

internal static class DnsZoneNameMappingReconcilerReadiness
{
    public const string MissingReconcilerDiagnosticCode =
        "dns.zone.nameMappingReconcilerMissing";

    public static bool IsMissing(
        IDnsZoneNameMappingReconciler? reconciler) =>
        reconciler is null or NoopDnsZoneNameMappingReconciler;

    public static string CreateMissingReconcilerReason(
        Resource resource)
    {
        var provider = resource.Attributes.GetString(
            DnsZoneResourceTypeProvider.Attributes.Provider);
        var providerDetail = string.IsNullOrWhiteSpace(provider)
            ? "No DNS name-mapping reconciler is registered."
            : $"No DNS name-mapping reconciler is registered for provider '{provider}'.";

        return $"{providerDetail} Register a DNS/name-mapping provider package before reconciling names for resource '{resource.Name}' ({resource.EffectiveResourceId}).";
    }

    public static ResourceDefinitionDiagnostic CreateMissingReconcilerDiagnostic(
        Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            MissingReconcilerDiagnosticCode,
            CreateMissingReconcilerReason(resource),
            resource.EffectiveResourceId);
}

public sealed class NoopDnsZoneNameMappingReconciler :
    IDnsZoneNameMappingReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default)
    =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            DnsZoneNameMappingReconcilerReadiness
                .CreateMissingReconcilerDiagnostic(resource)
        ]);
}
