namespace CloudShell.ControlPlane.Providers;

public interface IStorageInspector
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopStorageInspector :
    IStorageInspector
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            StorageInspectorReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class StorageInspectorReadiness
{
    public const string DiagnosticCode = "storage.inspectorMissing";

    public static bool IsMissing(IStorageInspector? inspector) =>
        inspector is null or NoopStorageInspector;

    public static string CreateMissingReason(Resource resource) =>
        $"Storage resource '{resource.EffectiveResourceId}' cannot be inspected because no storage inspector is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
