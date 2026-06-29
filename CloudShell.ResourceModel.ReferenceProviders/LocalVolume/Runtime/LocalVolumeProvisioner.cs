namespace CloudShell.ResourceModel.ReferenceProviders;

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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
