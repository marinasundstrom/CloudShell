namespace CloudShell.ControlPlane.Providers;

public interface IMacOSHostNetworkEndpointMappingReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed class NoopMacOSHostNetworkEndpointMappingReconciler :
    IMacOSHostNetworkEndpointMappingReconciler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
        Resource resource,
        ResourceProjectionExecutionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            MacOSHostNetworkEndpointMappingReconcilerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class MacOSHostNetworkEndpointMappingReconcilerReadiness
{
    public const string DiagnosticCode = "hostNetworking.macos.endpointMappingReconcilerMissing";

    public static bool IsMissing(IMacOSHostNetworkEndpointMappingReconciler? reconciler) =>
        reconciler is null or NoopMacOSHostNetworkEndpointMappingReconciler;

    public static string CreateMissingReason(Resource resource) =>
        $"macOS host network '{resource.EffectiveResourceId}' cannot reconcile endpoint mappings because no macOS host network endpoint-mapping reconciler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
