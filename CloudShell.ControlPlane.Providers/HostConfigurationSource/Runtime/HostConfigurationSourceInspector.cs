namespace CloudShell.ControlPlane.Providers;

public interface IHostConfigurationSourceInspector
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopHostConfigurationSourceInspector :
    IHostConfigurationSourceInspector
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            HostConfigurationSourceInspectorReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class HostConfigurationSourceInspectorReadiness
{
    public const string DiagnosticCode = "configuration.host.inspectorMissing";

    public static bool IsMissing(IHostConfigurationSourceInspector? inspector) =>
        inspector is null or NoopHostConfigurationSourceInspector;

    public static string CreateMissingReason(Resource resource) =>
        $"Host configuration source resource '{resource.EffectiveResourceId}' cannot be inspected because no host configuration source inspector is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
