namespace CloudShell.ControlPlane.Providers;

public interface IContainerHostInspector
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopContainerHostInspector :
    IContainerHostInspector
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            ContainerHostInspectorReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class ContainerHostInspectorReadiness
{
    public const string DiagnosticCode = "container.host.inspectorMissing";

    public static bool IsMissing(IContainerHostInspector? inspector) =>
        inspector is null or NoopContainerHostInspector;

    public static string CreateMissingReason(Resource resource) =>
        $"Container Host resource '{resource.EffectiveResourceId}' cannot be inspected because no Container Host inspector is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
