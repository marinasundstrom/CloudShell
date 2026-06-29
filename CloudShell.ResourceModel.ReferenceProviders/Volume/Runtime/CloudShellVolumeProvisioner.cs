namespace CloudShell.ResourceModel.ReferenceProviders;

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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
