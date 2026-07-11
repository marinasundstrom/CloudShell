using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Hosting;
using CloudShell.Client.Authentication;
using CloudShell.ControlPlane.ResourceManager.Identity;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ThirdPartyIdentity;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceModel.Resource;
using GraphResourceState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.Sample.Tests;

public sealed class ThirdPartyIdentityResourceModelSetupHandlerTests
{
    [Fact]
    public async Task ResourceManagerBridge_DelegatesToAttachedIdentityProviderSetup()
    {
        var declarations = new ResourceDeclarationStore();
        declarations.AddIdentityProvider(new ResourceIdentityProviderDefinition(
            "identity:keycloak",
            "Keycloak",
            ResourceIdentityProviderKind.Oidc,
            new Dictionary<string, string>
            {
                ["Provider"] = "Keycloak"
            },
            ProvisioningResourceId: "cloudshell.identity-provisioning:keycloak"));
        var setupHandler = new RecordingSetupHandler();
        var setupService = new ResourceIdentityProviderSetupService(
            declarations,
            new ResourceIdentityProviderCatalog(),
            [setupHandler]);
        using var services = CreateServices(setupService);
        var bridge = new ThirdPartyIdentityResourceManagerIdentitySetupBridge(
            services.GetRequiredService<IServiceScopeFactory>());

        var diagnostics = await bridge.SetupAsync(CreateResourceModelIdentityProvisioningResource(
            includeProviderId: true));

        Assert.Equal("identity:keycloak", setupHandler.SetupProviderId);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal("identity.provisioning.setupInformation", diagnostic.Code);
        Assert.Equal("Configured Resource model identity provider.", diagnostic.Message);
        Assert.Equal("identity:keycloak", diagnostic.Target);
    }

    [Fact]
    public async Task ResourceManagerBridge_WarnsWhenNoProviderIsAttached()
    {
        var setupService = new ResourceIdentityProviderSetupService(
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            []);
        using var services = CreateServices(setupService);
        var bridge = new ThirdPartyIdentityResourceManagerIdentitySetupBridge(
            services.GetRequiredService<IServiceScopeFactory>());

        var diagnostics = await bridge.SetupAsync(CreateResourceModelIdentityProvisioningResource(
            includeProviderId: false));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("identity.provisioning.providerMissing", diagnostic.Code);
        Assert.Contains("cloudshell.identity-provisioning:keycloak", diagnostic.Message);
    }

    [Fact]
    public async Task ResourceModelIdentityProvisioningSetupHandler_DelegatesToBridge()
    {
        var bridge = new RecordingResourceModelSetupBridge();
        var handler = new ThirdPartyIdentityResourceModelSetupHandler(bridge);
        var resource = CreateResourceModelIdentityProvisioningResource(includeProviderId: true);

        var diagnostics = await handler.SetupAsync(resource);

        Assert.Empty(diagnostics);
        Assert.Equal("cloudshell.identity-provisioning:keycloak", bridge.SetupResourceId);
    }

    [Fact]
    public async Task IdentityEnvironmentProvider_DerivesCredentialVariablesFromResourceManagerIdentityDeclaration()
    {
        var services = new ServiceCollection();
        var builder = services.AddCloudShellControlPlane();
        var declarations = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(ResourceDeclarationStore))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ResourceDeclarationStore>()
            .Single();
        var provider = declarations.AddIdentityProvider(new ResourceIdentityProviderDefinition(
            "identity:keycloak",
            "Keycloak",
            ResourceIdentityProviderKind.Oidc,
            new Dictionary<string, string>
            {
                ["Provider"] = "Keycloak"
            }));
        declarations.Declare(
            builder,
            "resource-model",
            "application.dotnet-app:keycloak-provisioned-api",
            identity: new ResourceIdentityBinding(
                provider.Id,
                Subject: "client:keycloak-provisioned-api",
                Scopes: ["openid"],
                Name: "keycloak-provisioned-api"));
        var environmentProvider = new ThirdPartyIdentityAspNetCoreProjectIdentityEnvironmentProvider(
            declarations,
            new ResourceIdentityProviderCatalog(),
            [new RecordingCredentialEnvironmentProvider()]);

        var variables = await environmentProvider.ResolveAsync(CreateResourceModelAspNetCoreProjectResource());

        Assert.Equal(
            "https://identity.example/token",
            variables[EnvironmentCloudShellResourceCredential.TokenEndpointEnvironmentVariable]);
        Assert.Equal(
            "application.dotnet-app:keycloak-provisioned-api/keycloak-provisioned-api",
            variables[EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable]);
        Assert.Equal(
            "openid",
            variables[EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable]);
    }

    private static GraphResource CreateResourceModelIdentityProvisioningResource(
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
                "identity:keycloak";
        }

        return resolver.Resolve(new GraphResourceState(
            "keycloak",
            IdentityProvisioningResourceTypeProvider.ResourceTypeId,
            ResourceId: "cloudshell.identity-provisioning:keycloak",
            ProviderId: IdentityProvisioningResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }

    private static GraphResource CreateResourceModelAspNetCoreProjectResource()
    {
        var provider = new AspNetCoreProjectResourceTypeProvider();
        var resolver = new ResourceResolver(
            [AspNetCoreProjectResourceTypeProvider.ClassDefinition],
            [provider.TypeDefinition],
            attributeValueShapeProviders:
            [
                new NetworkingEndpointShapeProvider(),
                new AspNetCoreProjectShapeProvider()
            ]);

        return resolver.Resolve(new GraphResourceState(
            "keycloak-provisioned-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ResourceId: "application.dotnet-app:keycloak-provisioned-api",
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    "samples/ThirdPartyIdentity/Api/CloudShell.ThirdPartyIdentity.Api.csproj"
            }));
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
                        "Configured Resource model identity provider.",
                        ProviderId: request.Provider.Id)
                ]));
        }
    }

    private sealed class RecordingResourceModelSetupBridge :
        IThirdPartyIdentityResourceModelSetupBridge
    {
        public string? SetupResourceId { get; private set; }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            SetupResourceId = resource.EffectiveResourceId;
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingCredentialEnvironmentProvider :
        IResourceIdentityCredentialEnvironmentProvider
    {
        public string ProviderId => "recording";

        public bool CanCreateEnvironment(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, "identity:keycloak", StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<EnvironmentVariableAssignment> CreateEnvironment(
            ResourceIdentityCredentialEnvironmentRequest request) =>
            [
                new(
                    EnvironmentCloudShellResourceCredential.TokenEndpointEnvironmentVariable,
                    "https://identity.example/token"),
                new(
                    EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable,
                    $"{request.Identity.ResourceId}/{request.Identity.Name}"),
                new(
                    EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable,
                    request.DefaultScope)
            ];
    }
}
