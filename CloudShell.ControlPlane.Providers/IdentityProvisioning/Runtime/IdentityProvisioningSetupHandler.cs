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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            IdentityProvisioningSetupHandlerReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class IdentityProvisioningSetupHandlerReadiness
{
    public const string DiagnosticCode = "identity.provisioning.setupHandlerMissing";

    public static bool IsMissing(IIdentityProvisioningSetupHandler? setupHandler) =>
        setupHandler is null or NoopIdentityProvisioningSetupHandler;

    public static string CreateMissingReason(Resource resource) =>
        $"Identity provisioning resource '{resource.EffectiveResourceId}' cannot set up an identity provider because no identity provisioning setup handler is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}
