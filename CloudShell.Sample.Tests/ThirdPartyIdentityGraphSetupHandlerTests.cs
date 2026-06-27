using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Hosting;
using CloudShell.Client.Authentication;
using CloudShell.ControlPlane.ResourceManager.Identity;
using CloudShell.Providers.Applications;
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
    public async Task ResourceManagerBridge_DelegatesToAttachedIdentityProviderSetup()
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
        var bridge = new ThirdPartyIdentityGraphResourceManagerIdentitySetupBridge(
            services.GetRequiredService<IServiceScopeFactory>());

        var diagnostics = await bridge.SetupAsync(CreateGraphIdentityProvisioningResource(
            includeProviderId: true));

        Assert.Equal("identity:graph-keycloak", setupHandler.SetupProviderId);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal("identity.provisioning.setupInformation", diagnostic.Code);
        Assert.Equal("Configured graph identity provider.", diagnostic.Message);
        Assert.Equal("identity:graph-keycloak", diagnostic.Target);
    }

    [Fact]
    public async Task ResourceManagerBridge_WarnsWhenNoProviderIsAttached()
    {
        var setupService = new ResourceIdentityProviderSetupService(
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            []);
        using var services = CreateServices(setupService);
        var bridge = new ThirdPartyIdentityGraphResourceManagerIdentitySetupBridge(
            services.GetRequiredService<IServiceScopeFactory>());

        var diagnostics = await bridge.SetupAsync(CreateGraphIdentityProvisioningResource(
            includeProviderId: false));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("identity.provisioning.providerMissing", diagnostic.Code);
        Assert.Contains("identity-provisioning:graph-keycloak", diagnostic.Message);
    }

    [Fact]
    public async Task GraphIdentityProvisioningSetupHandler_DelegatesToBridge()
    {
        var bridge = new RecordingGraphIdentityProvisioningSetupBridge();
        var handler = new GraphIdentityProvisioningSetupHandler(bridge);
        var resource = CreateGraphIdentityProvisioningResource(includeProviderId: true);

        var diagnostics = await handler.SetupAsync(resource);

        Assert.Empty(diagnostics);
        Assert.Equal("identity-provisioning:graph-keycloak", bridge.SetupResourceId);
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
            "identity:graph-keycloak",
            "Graph Keycloak",
            ResourceIdentityProviderKind.Oidc,
            new Dictionary<string, string>
            {
                ["Provider"] = "Keycloak"
            }));
        declarations.Declare(
            builder,
            "resource-model",
            "application.aspnet-core-project:graph-keycloak-provisioned-api",
            identity: new ResourceIdentityBinding(
                provider.Id,
                Subject: "client:graph-keycloak-provisioned-api",
                Scopes: ["openid"],
                Name: "graph-keycloak-provisioned-api"));
        var environmentProvider = new GraphAspNetCoreProjectIdentityEnvironmentProvider(
            declarations,
            new ResourceIdentityProviderCatalog(),
            [new RecordingCredentialEnvironmentProvider()],
            new ApplicationProviderOptions());

        var variables = await environmentProvider.ResolveAsync(CreateGraphAspNetCoreProjectResource());

        Assert.Equal(
            "https://identity.example/token",
            variables[EnvironmentCloudShellResourceCredential.TokenEndpointEnvironmentVariable]);
        Assert.Equal(
            "application.aspnet-core-project:graph-keycloak-provisioned-api/graph-keycloak-provisioned-api",
            variables[EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable]);
        Assert.Equal(
            "openid",
            variables[EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable]);
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

    private static GraphResource CreateGraphAspNetCoreProjectResource()
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
            "graph-keycloak-provisioned-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ResourceId: "application.aspnet-core-project:graph-keycloak-provisioned-api",
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
                        "Configured graph identity provider.",
                        ProviderId: request.Provider.Id)
                ]));
        }
    }

    private sealed class RecordingGraphIdentityProvisioningSetupBridge :
        IThirdPartyIdentityGraphIdentityProvisioningSetupBridge
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
            string.Equals(provider.Id, "identity:graph-keycloak", StringComparison.OrdinalIgnoreCase);

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
