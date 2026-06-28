using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Identity;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ThirdPartyIdentity;

public sealed class ThirdPartyIdentityResourceModelSetupHandler(
    IThirdPartyIdentityResourceModelSetupBridge bridge) :
    IIdentityProvisioningSetupHandler
{
    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return await bridge.SetupAsync(resource, cancellationToken);
    }
}

public sealed class ThirdPartyIdentityResourceManagerIdentitySetupBridge(
    IServiceScopeFactory scopeFactory) : IThirdPartyIdentityResourceModelSetupBridge
{
    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        using var scope = scopeFactory.CreateScope();
        var setupService = scope.ServiceProvider.GetRequiredService<ResourceIdentityProviderSetupService>();
        var providerId = resource.Attributes.GetString(
            IdentityProvisioningResourceTypeProvider.Attributes.IdentityProviderId);
        var provider = !string.IsNullOrWhiteSpace(providerId)
            ? setupService.ResolveProvider(providerId)
            : setupService.ResolveProviderForProvisioningResource(resource.EffectiveResourceId);
        if (provider is null)
        {
            return
            [
                ResourceDefinitionDiagnostic.Warning(
                    "identity.provisioning.providerMissing",
                    $"No resource identity provider is attached to provisioning resource '{resource.EffectiveResourceId}'.",
                    resource.EffectiveResourceId)
            ];
        }

        var result = await setupService.SetupAsync(
            provider.Id,
            cancellationToken);
        return result.SetupDiagnostics
            .Select(diagnostic => ToResourceDiagnostic(provider, diagnostic))
            .ToArray();
    }

    private static ResourceDefinitionDiagnostic ToResourceDiagnostic(
        ResourceIdentityProviderDefinition provider,
        ResourceIdentityProvisioningDiagnostic diagnostic) =>
        new(
            ToResourceSeverity(diagnostic.Severity),
            ToDiagnosticCode(diagnostic.Severity),
            diagnostic.Message,
            provider.Id);

    private static ResourceDefinitionDiagnosticSeverity ToResourceSeverity(
        ResourceIdentityProvisioningDiagnosticSeverity severity) =>
        severity switch
        {
            ResourceIdentityProvisioningDiagnosticSeverity.Information =>
                ResourceDefinitionDiagnosticSeverity.Information,
            ResourceIdentityProvisioningDiagnosticSeverity.Warning =>
                ResourceDefinitionDiagnosticSeverity.Warning,
            ResourceIdentityProvisioningDiagnosticSeverity.Error =>
                ResourceDefinitionDiagnosticSeverity.Error,
            _ => ResourceDefinitionDiagnosticSeverity.Warning
        };

    private static string ToDiagnosticCode(
        ResourceIdentityProvisioningDiagnosticSeverity severity) =>
        severity switch
        {
            ResourceIdentityProvisioningDiagnosticSeverity.Information =>
                "identity.provisioning.setupInformation",
            ResourceIdentityProvisioningDiagnosticSeverity.Warning =>
                "identity.provisioning.setupWarning",
            ResourceIdentityProvisioningDiagnosticSeverity.Error =>
                "identity.provisioning.setupError",
            _ => "identity.provisioning.setupDiagnostic"
        };
}
