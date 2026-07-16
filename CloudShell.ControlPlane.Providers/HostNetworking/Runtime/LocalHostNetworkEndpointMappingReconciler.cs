namespace CloudShell.ControlPlane.Providers;

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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            LocalHostNetworkEndpointMappingReconcilerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class LocalHostNetworkEndpointMappingReconcilerReadiness
{
    public const string DiagnosticCode = "hostNetworking.endpointMappingReconcilerMissing";

    public static bool IsMissing(ILocalHostNetworkEndpointMappingReconciler? reconciler) =>
        reconciler is null or NoopLocalHostNetworkEndpointMappingReconciler;

    public static string CreateMissingReason(Resource resource) =>
        $"Local host network '{resource.EffectiveResourceId}' cannot reconcile endpoint mappings because no local host network endpoint-mapping reconciler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
