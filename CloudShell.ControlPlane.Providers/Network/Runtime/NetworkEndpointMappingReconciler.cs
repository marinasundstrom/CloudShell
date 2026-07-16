namespace CloudShell.ControlPlane.Providers;

public interface INetworkEndpointMappingReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class NoopNetworkEndpointMappingReconciler :
    INetworkEndpointMappingReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            NetworkEndpointMappingReconcilerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class NetworkEndpointMappingReconcilerReadiness
{
    public const string DiagnosticCode = "network.endpointMappingReconcilerMissing";

    public static bool IsMissing(INetworkEndpointMappingReconciler? reconciler) =>
        reconciler is null or NoopNetworkEndpointMappingReconciler;

    public static string CreateMissingReason(Resource resource) =>
        $"Network '{resource.EffectiveResourceId}' cannot reconcile endpoint mappings because no network endpoint-mapping reconciler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
