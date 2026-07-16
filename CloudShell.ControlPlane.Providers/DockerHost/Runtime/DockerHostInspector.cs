namespace CloudShell.ControlPlane.Providers;

public interface IDockerHostInspector
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopDockerHostInspector :
    IDockerHostInspector
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            DockerHostInspectorReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class DockerHostInspectorReadiness
{
    public const string DiagnosticCode = "docker.host.inspectorMissing";

    public static bool IsMissing(IDockerHostInspector? inspector) =>
        inspector is null or NoopDockerHostInspector;

    public static string CreateMissingReason(Resource resource) =>
        $"Docker Host resource '{resource.EffectiveResourceId}' cannot be inspected because no Docker Host inspector is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
