using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Identity;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ThirdPartyIdentity;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceDefinitions.Resource;
using GraphResourceState = CloudShell.ResourceDefinitions.ResourceState;

namespace CloudShell.Sample.Tests;

public sealed class ThirdPartyIdentityGraphSetupHandlerTests
{
    [Fact]
    public async Task GraphIdentityProvisioningSetupHandler_DelegatesToAttachedIdentityProviderSetup()
    {
        var declarations = new ResourceDeclarationStore();
        declarations.AddIdentityProvider(new ResourceIdentityProviderDefinition(
            "identity:graph-keycloak",
            "Graph Keycloak",
            ResourceIdentityProviderKind.Oidc,
            new Dictionary<string, string>
            {
                ["Provider"] = "Keycloak"
            },
            ProvisioningResourceId: "identity-provisioning:graph-keycloak"));
        var setupHandler = new RecordingSetupHandler();
        var setupService = new ResourceIdentityProviderSetupService(
            declarations,
            new ResourceIdentityProviderCatalog(),
            [setupHandler]);
        using var services = CreateServices(setupService);
        var handler = new GraphIdentityProvisioningSetupHandler(
            services.GetRequiredService<IServiceScopeFactory>());

        var diagnostics = await handler.SetupAsync(CreateGraphIdentityProvisioningResource(
            includeProviderId: true));

        Assert.Equal("identity:graph-keycloak", setupHandler.SetupProviderId);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal("identity.provisioning.setupInformation", diagnostic.Code);
        Assert.Equal("Configured graph identity provider.", diagnostic.Message);
        Assert.Equal("identity:graph-keycloak", diagnostic.Target);
    }

    [Fact]
    public async Task GraphIdentityProvisioningSetupHandler_WarnsWhenNoProviderIsAttached()
    {
        var setupService = new ResourceIdentityProviderSetupService(
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            []);
        using var services = CreateServices(setupService);
        var handler = new GraphIdentityProvisioningSetupHandler(
            services.GetRequiredService<IServiceScopeFactory>());

        var diagnostics = await handler.SetupAsync(CreateGraphIdentityProvisioningResource(
            includeProviderId: false));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("identity.provisioning.providerMissing", diagnostic.Code);
        Assert.Contains("identity-provisioning:graph-keycloak", diagnostic.Message);
    }

    private static GraphResource CreateGraphIdentityProvisioningResource(
        bool includeProviderId)
    {
        var provider = new IdentityProvisioningResourceTypeProvider();
        var resolver = new ResourceResolver(
            [IdentityProvisioningResourceTypeProvider.ClassDefinition],
            [provider.TypeDefinition]);
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider] = "Keycloak"
        };
        if (includeProviderId)
        {
            attributes[IdentityProvisioningResourceTypeProvider.Attributes.IdentityProviderId] =
                "identity:graph-keycloak";
        }

        return resolver.Resolve(new GraphResourceState(
            "graph-keycloak",
            IdentityProvisioningResourceTypeProvider.ResourceTypeId,
            ResourceId: "identity-provisioning:graph-keycloak",
            ProviderId: IdentityProvisioningResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }

    private static ServiceProvider CreateServices(
        ResourceIdentityProviderSetupService setupService)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => setupService);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingSetupHandler : IResourceIdentityProviderSetupHandler
    {
        public string ProviderId => "keycloak";

        public string? SetupProviderId { get; private set; }

        public bool CanSetup(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.ProviderSettings.GetValueOrDefault("Provider"), "Keycloak", StringComparison.OrdinalIgnoreCase);

        public Task<ResourceIdentityProviderSetupResult> SetupAsync(
            ResourceIdentityProviderSetupRequest request,
            CancellationToken cancellationToken = default)
        {
            SetupProviderId = request.Provider.Id;
            return Task.FromResult(new ResourceIdentityProviderSetupResult(
                request.Provider.Id,
                [
                    new ResourceIdentityProvisioningDiagnostic(
                        ResourceIdentityProvisioningDiagnosticSeverity.Information,
                        "Configured graph identity provider.",
                        ProviderId: request.Provider.Id)
                ]));
        }
    }
}
