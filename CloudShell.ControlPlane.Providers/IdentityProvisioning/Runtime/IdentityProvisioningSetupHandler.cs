namespace CloudShell.ControlPlane.Providers;

public interface IIdentityProvisioningSetupHandler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopIdentityProvisioningSetupHandler :
    IIdentityProvisioningSetupHandler
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
