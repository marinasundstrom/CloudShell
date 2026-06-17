using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.Tests;

public sealed class ResourceIdentityProvisioningServiceTests
{
    [Fact]
    public void CreatePlan_GroupsResolvedIdentitiesByProviderAndIncludesMatchingGrants()
    {
        var declarations = new ResourceDeclarationStore();
        var builder = new TestCloudShellBuilder();
        declarations.Declare(
            builder,
            "test",
            "api",
            identity: new ResourceIdentityBinding("identity:dev", Name: "api-service"));
        declarations.Declare(
            builder,
            "test",
            "worker",
            identity: ResourceIdentityBinding.RequireIdentity(["queue.read"]) with { Name = "worker-service" });
        declarations.AddIdentityProvider(
            new ResourceIdentityProviderDefinition(
                "identity:dev",
                "Development",
                ResourceIdentityProviderKind.BuiltIn),
            useAsDefault: true);
        declarations.Declare(builder, "test", "database");
        declarations.AddPermissionGrant(new ResourcePermissionGrant(
            ResourceIdentityReference.ForResource("api", "api-service"),
            "database",
            "Database/databases/readWrite/action"));
        declarations.AddPermissionGrant(new ResourcePermissionGrant(
            ResourceIdentityReference.ForResource("worker"),
            "queue",
            "Queue/queues/read/action"));
        declarations.AddPermissionGrant(new ResourcePermissionGrant(
            ResourceIdentityReference.ForResource("missing", "missing-service"),
            "database",
            "Database/databases/read/action"));

        var service = new ResourceIdentityProvisioningService(
            declarations,
            new ResourceIdentityProviderCatalog(),
            []);

        var plan = service.CreatePlan();

        var request = Assert.Single(plan.Requests);
        Assert.Equal("identity:dev", request.Provider.Id);
        Assert.Equal(
            ["api:api-service", "worker:worker-service"],
            request.Identities.Select(entry => $"{entry.Identity.ResourceId}:{entry.Identity.Name}").ToArray());
        Assert.Equal(
            ["api:api-service:Database/databases/readWrite/action", "worker::Queue/queues/read/action"],
            request.PermissionGrants
                .Select(grant => $"{grant.Identity.ResourceId}:{grant.Identity.Name}:{grant.Permission}")
                .ToArray());
        Assert.Empty(plan.Diagnostics);
    }

    [Fact]
    public void CreatePlan_ReportsUnresolvedIdentityProviders()
    {
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            "test",
            "api",
            identity: ResourceIdentityBinding.RequireIdentity() with { Name = "api-service" });

        var service = new ResourceIdentityProvisioningService(
            declarations,
            new ResourceIdentityProviderCatalog(),
            []);

        var plan = service.CreatePlan();

        Assert.Empty(plan.Requests);
        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(ResourceIdentityProvisioningDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("api", diagnostic.Identity?.ResourceId);
        Assert.Equal("No default resource identity provider is registered.", diagnostic.Message);
    }

    [Fact]
    public async Task ProvisionAsync_InvokesMatchingProvisionerAndReportsMissingProvisioner()
    {
        var declarations = new ResourceDeclarationStore();
        var builder = new TestCloudShellBuilder();
        declarations.Declare(
            builder,
            "test",
            "api",
            identity: new ResourceIdentityBinding("identity:dev", Name: "api-service"));
        declarations.Declare(
            builder,
            "test",
            "worker",
            identity: new ResourceIdentityBinding("identity:entra", Name: "worker-service"));
        var provisioner = new RecordingProvisioner("identity:dev");

        var service = new ResourceIdentityProvisioningService(
            declarations,
            new ResourceIdentityProviderCatalog(
                [
                    new("identity:dev", "Development", ResourceIdentityProviderKind.BuiltIn),
                    new("identity:entra", "Microsoft Entra ID", ResourceIdentityProviderKind.Oidc)
                ]),
            [provisioner]);

        var result = await service.ProvisionAsync();

        var request = Assert.Single(provisioner.Requests);
        Assert.Equal("identity:dev", request.Provider.Id);
        Assert.Equal("api", Assert.Single(request.Identities).Identity.ResourceId);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceIdentityProvisioningDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("identity:entra", diagnostic.ProviderId);
    }

    [Fact]
    public async Task GetResourceStatusAsync_InvokesMatchingStatusProvider()
    {
        var declarations = new ResourceDeclarationStore();
        var builder = new TestCloudShellBuilder();
        declarations.Declare(
            builder,
            "test",
            "api",
            identity: new ResourceIdentityBinding("identity:dev", Name: "api-service"));
        var provisioner = new RecordingProvisioner("identity:dev");

        var service = new ResourceIdentityProvisioningService(
            declarations,
            new ResourceIdentityProviderCatalog(
                [
                    new("identity:dev", "Development", ResourceIdentityProviderKind.BuiltIn)
                ]),
            [],
            [provisioner]);

        var result = await service.GetResourceStatusAsync("api");

        var request = Assert.Single(provisioner.StatusRequests);
        Assert.Equal("identity:dev", request.Provider.Id);
        var status = Assert.Single(result.Statuses);
        Assert.Equal("api", status.Identity.ResourceId);
        Assert.Equal(ResourceIdentityProvisioningState.Provisioned, status.State);
        Assert.Empty(result.ProvisioningDiagnostics);
    }

    [Fact]
    public async Task GetResourceStatusAsync_ReportsMissingStatusProvider()
    {
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            "test",
            "api",
            identity: new ResourceIdentityBinding("identity:keycloak", Name: "api-service"));
        var service = new ResourceIdentityProvisioningService(
            declarations,
            new ResourceIdentityProviderCatalog(
                [
                    new("identity:keycloak", "Keycloak", ResourceIdentityProviderKind.Oidc)
                ]),
            []);

        var result = await service.GetResourceStatusAsync("api");

        Assert.Equal("identity:keycloak", result.ProviderId);
        var status = Assert.Single(result.Statuses);
        Assert.Equal(ResourceIdentityProvisioningState.Unknown, status.State);
        Assert.Equal("Provisioning status is not available for this identity provider.", status.Detail);
        var diagnostic = Assert.Single(result.ProvisioningDiagnostics);
        Assert.Equal(ResourceIdentityProvisioningDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("identity:keycloak", diagnostic.ProviderId);
        Assert.Equal(
            "No resource identity provisioning status provider is registered for provider 'identity:keycloak'.",
            diagnostic.Message);
    }

    [Fact]
    public async Task SetupAsync_InvokesMatchingSetupHandler()
    {
        var declarations = new ResourceDeclarationStore();
        declarations.AddIdentityProvider(new ResourceIdentityProviderDefinition(
            "identity:dev",
            "Development",
            ResourceIdentityProviderKind.BuiltIn));
        var setupHandler = new RecordingSetupHandler("identity:dev");
        var service = new ResourceIdentityProviderSetupService(
            declarations,
            new ResourceIdentityProviderCatalog(),
            [setupHandler]);

        var result = await service.SetupAsync("identity:dev");

        Assert.Equal("identity:dev", result.ProviderId);
        var request = Assert.Single(setupHandler.Requests);
        Assert.Equal("identity:dev", request.Provider.Id);
        Assert.Empty(result.SetupDiagnostics);
    }

    [Fact]
    public async Task SetupAsync_ReportsMissingSetupHandler()
    {
        var service = new ResourceIdentityProviderSetupService(
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(
                [new("identity:entra", "Microsoft Entra ID", ResourceIdentityProviderKind.Oidc)]),
            []);

        var result = await service.SetupAsync("identity:entra");

        Assert.Equal("identity:entra", result.ProviderId);
        var diagnostic = Assert.Single(result.SetupDiagnostics);
        Assert.Equal(ResourceIdentityProvisioningDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("identity:entra", diagnostic.ProviderId);
    }

    [Fact]
    public async Task ProvisioningResourceAction_InvokesAttachedProviderSetup()
    {
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            ResourceIdentityProvisioningResources.ProviderId,
            "identity-provisioning:keycloak",
            resourceClass: ResourceClass.Infrastructure,
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.InfrastructureKind] = "identity-provisioning",
                ["identity.provider"] = "Keycloak"
            });
        declarations.AddIdentityProvider(
            new ResourceIdentityProviderDefinition(
                "identity:keycloak",
                "Keycloak",
                ResourceIdentityProviderKind.Oidc,
                ProvisioningResourceId: "identity-provisioning:keycloak"));
        var setupHandler = new RecordingSetupHandler("identity:keycloak");
        var setupService = new ResourceIdentityProviderSetupService(
            declarations,
            new ResourceIdentityProviderCatalog(),
            [setupHandler]);
        var provider = new ResourceIdentityProvisioningResourceProvider(declarations, setupService);
        var resource = Assert.Single(provider.GetResources());
        Assert.NotNull(resource.Actions);
        var action = Assert.Single(resource.Actions);

        var result = await provider.ExecuteActionAsync(
            new ResourceProcedureContext(resource, null, null, new EmptyResourceRegistrationStore()),
            action);

        Assert.Equal("Set up identity provider 'identity:keycloak'.", result.Message);
        var request = Assert.Single(setupHandler.Requests);
        Assert.Equal("identity:keycloak", request.Provider.Id);
    }

    private sealed class RecordingProvisioner(string providerId) :
        IResourceIdentityProvisioner,
        IResourceIdentityProvisioningStatusProvider
    {
        public List<ResourceIdentityProvisioningRequest> Requests { get; } = [];

        public List<ResourceIdentityProvisioningRequest> StatusRequests { get; } = [];

        public string ProviderId => providerId;

        public bool CanProvision(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, ProviderId, StringComparison.OrdinalIgnoreCase);

        public bool CanGetProvisioningStatus(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, ProviderId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceIdentityProvisioningResult> ProvisionAsync(
            ResourceIdentityProvisioningRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ResourceIdentityProvisioningResult(request.Provider.Id));
        }

        public Task<ResourceIdentityProvisioningStatusResult> GetProvisioningStatusAsync(
            ResourceIdentityProvisioningRequest request,
            CancellationToken cancellationToken = default)
        {
            StatusRequests.Add(request);
            return Task.FromResult(new ResourceIdentityProvisioningStatusResult(
                request.Provider.Id,
                request.Identities
                    .Select(entry => new ResourceIdentityProvisioningStatus(
                        entry.Identity,
                        ResourceIdentityProvisioningState.Provisioned))
                    .ToArray()));
        }
    }

    private sealed class RecordingSetupHandler(string providerId) : IResourceIdentityProviderSetupHandler
    {
        public List<ResourceIdentityProviderSetupRequest> Requests { get; } = [];

        public string ProviderId => providerId;

        public bool CanSetup(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, ProviderId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceIdentityProviderSetupResult> SetupAsync(
            ResourceIdentityProviderSetupRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ResourceIdentityProviderSetupResult(request.Provider.Id));
        }
    }

    private sealed class EmptyResourceRegistrationStore : IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() => [];

        public ResourceRegistration? GetRegistration(string resourceId) => null;

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestCloudShellBuilder : ICloudShellBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
    }
}
