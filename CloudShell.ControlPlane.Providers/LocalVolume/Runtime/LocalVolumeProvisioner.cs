namespace CloudShell.ControlPlane.Providers;

public interface ILocalVolumeProvisioner
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopLocalVolumeProvisioner :
    ILocalVolumeProvisioner
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            LocalVolumeProvisionerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class LocalVolumeProvisionerReadiness
{
    public const string DiagnosticCode = "storage.volume.provisionerMissing";

    public static bool IsMissing(ILocalVolumeProvisioner? provisioner) =>
        provisioner is null or NoopLocalVolumeProvisioner;

    public static string CreateMissingReason(Resource resource) =>
        $"Local volume '{resource.EffectiveResourceId}' cannot be provisioned because no local volume provisioner is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
