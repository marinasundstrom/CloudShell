namespace CloudShell.ControlPlane.Providers;

public interface ICloudShellVolumeProvisioner
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopCloudShellVolumeProvisioner :
    ICloudShellVolumeProvisioner
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            CloudShellVolumeProvisionerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class CloudShellVolumeProvisionerReadiness
{
    public const string DiagnosticCode = "cloudshell.volume.provisionerMissing";

    public static bool IsMissing(ICloudShellVolumeProvisioner? provisioner) =>
        provisioner is null or NoopCloudShellVolumeProvisioner;

    public static string CreateMissingReason(Resource resource) =>
        $"CloudShell volume '{resource.EffectiveResourceId}' cannot be provisioned because no CloudShell volume provisioner is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
