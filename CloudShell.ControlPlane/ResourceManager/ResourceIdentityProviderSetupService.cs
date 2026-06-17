using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceIdentityProviderSetupService(
    ResourceDeclarationStore declarations,
    ResourceIdentityProviderCatalog identityProviders,
    IEnumerable<IResourceIdentityProviderSetupHandler> setupHandlers)
{
    private readonly IReadOnlyList<IResourceIdentityProviderSetupHandler> setupHandlers =
        setupHandlers.ToArray();

    public ResourceIdentityProviderDefinition ResolveProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        var effectiveProviders = declarations.CreateIdentityProviderCatalog(identityProviders);
        return effectiveProviders.GetProvider(providerId)
            ?? throw new ControlPlaneException(ControlPlaneError.InvalidRequest(
                $"Resource identity provider '{providerId}' is not registered."));
    }

    public ResourceIdentityProviderDefinition? ResolveProviderForProvisioningResource(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var effectiveProviders = declarations.CreateIdentityProviderCatalog(identityProviders);
        return effectiveProviders.Providers.FirstOrDefault(provider =>
            string.Equals(provider.ProvisioningResourceId, resourceId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ResourceIdentityProviderSetupResult> SetupAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider(providerId);
        var setupHandler = setupHandlers.FirstOrDefault(handler => handler.CanSetup(provider));
        if (setupHandler is null)
        {
            return new ResourceIdentityProviderSetupResult(
                provider.Id,
                [
                    new ResourceIdentityProvisioningDiagnostic(
                        ResourceIdentityProvisioningDiagnosticSeverity.Warning,
                        $"No resource identity provider setup handler is registered for provider '{provider.Id}'.",
                        ProviderId: provider.Id)
                ]);
        }

        return await setupHandler.SetupAsync(
            new ResourceIdentityProviderSetupRequest(provider),
            cancellationToken);
    }
}
