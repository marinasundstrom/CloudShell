namespace CloudShell.ControlPlane.Providers;

public interface IVirtualNetworkEndpointMappingReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class NoopVirtualNetworkEndpointMappingReconciler :
    IVirtualNetworkEndpointMappingReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            VirtualNetworkEndpointMappingReconcilerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class VirtualNetworkEndpointMappingReconcilerReadiness
{
    public const string DiagnosticCode = "network.virtual.endpointMappingReconcilerMissing";

    public static bool IsMissing(IVirtualNetworkEndpointMappingReconciler? reconciler) =>
        reconciler is null or NoopVirtualNetworkEndpointMappingReconciler;

    public static string CreateMissingReason(Resource resource) =>
        $"Virtual network '{resource.EffectiveResourceId}' cannot reconcile endpoint mappings because no virtual network endpoint-mapping reconciler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
