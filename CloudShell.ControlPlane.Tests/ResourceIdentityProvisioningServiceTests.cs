using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            ResourcePrincipalReference.ForResourceIdentity("api", "api-service"),
            "database",
            "Database/databases/readWrite/action"));
        declarations.AddPermissionGrant(new ResourcePermissionGrant(
            ResourcePrincipalReference.ForResourceIdentity("worker"),
            "queue",
            "Queue/queues/read/action"));
        declarations.AddPermissionGrant(new ResourcePermissionGrant(
            ResourcePrincipalReference.ForResourceIdentity("missing", "missing-service"),
            "database",
            "Database/databases/read/action"));

        var service = new ResourceIdentityProvisioningService(
            declarations,
            new EmptyResourceRegistrationStore(),
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
                .Select(grant => $"{grant.ResourceIdentity!.ResourceId}:{grant.ResourceIdentity.Name}:{grant.Permission}")
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
            new EmptyResourceRegistrationStore(),
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
    public void CreatePlan_UsesRegistrationIdentityBindings()
    {
        var declarations = new ResourceDeclarationStore();
        declarations.AddIdentityProvider(
            new ResourceIdentityProviderDefinition(
                "identity:dev",
                "Development",
                ResourceIdentityProviderKind.BuiltIn),
            useAsDefault: true);
        declarations.AddPermissionGrant(new ResourcePermissionGrant(
            ResourcePrincipalReference.ForResourceIdentity("registered"),
            "database",
            "Database/databases/read/action"));
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    "registered",
                    "test",
                    null,
                    DateTimeOffset.UtcNow,
                    [],
                    ResourceIdentityBinding.RequireIdentity())
            ]);
        var service = new ResourceIdentityProvisioningService(
            declarations,
            registrations,
            new ResourceIdentityProviderCatalog(),
            []);

        var plan = service.CreatePlan();

        var request = Assert.Single(plan.Requests);
        Assert.Equal("identity:dev", request.Provider.Id);
        var identity = Assert.Single(request.Identities);
        Assert.Equal("registered", identity.Identity.ResourceId);
        Assert.Equal(ResourceIdentityBindingKind.Required, identity.Binding.Kind);
        var grant = Assert.Single(request.PermissionGrants);
        Assert.Equal("registered", grant.Principal.SourceResourceId);
        Assert.Empty(plan.Diagnostics);
    }

    [Fact]
    public void CreatePlan_ExcludesNonResourcePrincipalGrantsFromResourceIdentityProvisioning()
    {
        var declarations = new ResourceDeclarationStore();
        var builder = new TestCloudShellBuilder();
        declarations.Declare(
            builder,
            "test",
            "api",
            identity: new ResourceIdentityBinding("identity:dev", Name: "api-service"));
        declarations.AddIdentityProvider(
            new ResourceIdentityProviderDefinition(
                "identity:dev",
                "Development",
                ResourceIdentityProviderKind.BuiltIn),
            useAsDefault: true);
        declarations.AddPermissionGrant(new ResourcePermissionGrant(
            ResourcePrincipalReference.ForResourceIdentity("api", "api-service"),
            "configuration:app",
            ConfigurationStoreResourceOperationPermissions.ReadEntries));
        declarations.AddPermissionGrant(new ResourcePermissionGrant(
            new ResourcePrincipalReference(
                ResourcePrincipalKind.User,
                "alice",
                "Alice Local Developer",
                "identity:dev"),
            "configuration:app",
            CloudShellPermissions.Resources.Manage));

        var service = new ResourceIdentityProvisioningService(
            declarations,
            new EmptyResourceRegistrationStore(),
            new ResourceIdentityProviderCatalog(),
            []);

        var plan = service.CreatePlan();

        var request = Assert.Single(plan.Requests);
        var grant = Assert.Single(request.PermissionGrants);
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, grant.Principal.Kind);
        Assert.Equal("api", grant.Principal.SourceResourceId);
        Assert.Equal(ConfigurationStoreResourceOperationPermissions.ReadEntries, grant.Permission);
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
            new EmptyResourceRegistrationStore(),
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
            new EmptyResourceRegistrationStore(),
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
    public async Task BuiltInProvisioner_ListsProvisionedResourceIdentityPrincipals()
    {
        var registry = new BuiltInResourceIdentityRegistry();
        var provisioner = new BuiltInResourceIdentityProvisioner(registry);
        var provider = new ResourceIdentityProviderDefinition(
            "identity:dev",
            "Development",
            ResourceIdentityProviderKind.BuiltIn);
        var identity = ResourceIdentityReference.ForResource("api", "api-service");
        await provisioner.ProvisionAsync(
            new ResourceIdentityProvisioningRequest(
                provider,
                [
                    new ResourceIdentityProvisioningEntry(
                        identity,
                        new ResourceIdentityBinding("identity:dev", Name: "api-service"))
                ],
                [
                    new ResourcePermissionGrant(
                        identity.ToPrincipal(),
                        "configuration:app",
                        ConfigurationStoreResourceOperationPermissions.ReadEntries)
                ]));

        var result = await provisioner.QueryDirectoryAsync(
            new ResourceIdentityDirectoryRequest(
                provider,
                new ResourceIdentityDirectoryQuery(
                    "api-service",
                    new HashSet<ResourcePrincipalKind>
                    {
                        ResourcePrincipalKind.ResourceIdentity
                    })));

        var principal = Assert.Single(result.Principals);
        Assert.Equal("identity:dev", result.ProviderId);
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, principal.Reference.Kind);
        Assert.Equal("api/identities/api-service", principal.Reference.Id);
        Assert.Equal("api", principal.Reference.SourceResourceId);
        Assert.Equal("api-service", principal.Reference.SourceIdentityName);
        Assert.Equal("api/api-service", principal.PrincipalAttributes["clientId"]);
    }

    [Fact]
    public async Task BuiltInProvisioner_DiagnosticUsesResourceNameInsteadOfClientId()
    {
        var registry = new BuiltInResourceIdentityRegistry();
        var provisioner = new BuiltInResourceIdentityProvisioner(registry);
        var provider = new ResourceIdentityProviderDefinition(
            "identity:dev",
            "Development",
            ResourceIdentityProviderKind.BuiltIn);
        var identity = ResourceIdentityReference.ForResource(
            "configuration:application-topology",
            "application-topology");

        var result = await provisioner.ProvisionAsync(
            new ResourceIdentityProvisioningRequest(
                provider,
                [
                    new ResourceIdentityProvisioningEntry(
                        identity,
                        new ResourceIdentityBinding("identity:dev", Name: "application-topology"))
                ],
                []));

        var diagnostic = Assert.Single(result.ProvisioningDiagnostics);
        Assert.Equal(
            "Provisioned built-in resource identity client for resource 'application-topology'.",
            diagnostic.Message);
    }

    [Fact]
    public async Task BuiltInProvisioner_ListsInMemoryUserPrincipals()
    {
        var users = new InMemoryIdentitySetupOptions();
        users.IsConfigured = true;
        users.Users.Add(
            "Alice",
            password: "CloudShell123!",
            displayName: "Alice Local Developer",
            email: "alice@example.test");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(users);
        services.AddSingleton(new ResourceDeclarationStore());
        services.AddSingleton(Options.Create(new CloudShellAuthenticationOptions()));
        services.AddSingleton<InMemoryIdentityStore>();
        services.AddScoped<IUserStore<IdentityUser>>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryIdentityStore>());
        services.AddScoped<IRoleStore<IdentityRole>>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryIdentityStore>());
        services.AddIdentity<IdentityUser, IdentityRole>();
        services.AddScoped<CloudShellIdentitySeeder>();
        await using var serviceProvider = services.BuildServiceProvider();
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<CloudShellIdentitySeeder>()
                .SeedAsync();
        }

        var provisioner = new BuiltInResourceIdentityProvisioner(
            new BuiltInResourceIdentityRegistry(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            users);
        var provider = new ResourceIdentityProviderDefinition(
            "identity:dev",
            "Development",
            ResourceIdentityProviderKind.BuiltIn);

        var result = await provisioner.QueryDirectoryAsync(
            new ResourceIdentityDirectoryRequest(
                provider,
                new ResourceIdentityDirectoryQuery(
                    "alice",
                    new HashSet<ResourcePrincipalKind>
                    {
                        ResourcePrincipalKind.User
                    })));

        var principal = Assert.Single(result.Principals);
        Assert.Equal("identity:dev", result.ProviderId);
        Assert.Equal(ResourcePrincipalKind.User, principal.Reference.Kind);
        Assert.Equal("alice", principal.Reference.Id);
        Assert.Equal("Alice Local Developer", principal.Reference.DisplayName);
        Assert.Equal("identity:dev", principal.Reference.ProviderId);
        Assert.Equal("Alice Local Developer", principal.DisplayName);
        Assert.Equal("Built-in local user.", principal.Description);
        Assert.Equal("alice@example.test", principal.PrincipalAttributes["userName"]);
        Assert.False(string.IsNullOrWhiteSpace(principal.PrincipalAttributes["userId"]));
        Assert.Equal("alice@example.test", principal.PrincipalAttributes["email"]);
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
            new EmptyResourceRegistrationStore(),
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

    [Fact]
    public async Task ProvisioningResourceActionAvailability_ReportsMissingAttachedProvider()
    {
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            ResourceIdentityProvisioningResources.ProviderId,
            "identity-provisioning:orphan",
            resourceClass: ResourceClass.Infrastructure,
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.InfrastructureKind] = "identity-provisioning",
                ["identity.provider"] = "Orphan"
            });
        var setupService = new ResourceIdentityProviderSetupService(
            declarations,
            new ResourceIdentityProviderCatalog(),
            []);
        var provider = new ResourceIdentityProvisioningResourceProvider(declarations, setupService);
        var resource = Assert.Single(provider.GetResources());
        Assert.NotNull(resource.Actions);
        var action = Assert.Single(resource.Actions);

        var reason = await provider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(resource, null, null, new EmptyResourceRegistrationStore()),
            action);

        Assert.Equal(
            "No resource identity provider is attached to provisioning resource 'identity-provisioning:orphan'.",
            reason);
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

    private sealed class TestResourceRegistrationStore(IReadOnlyList<ResourceRegistration> registrations) :
        IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() => registrations;

        public ResourceRegistration? GetRegistration(string resourceId) =>
            registrations.FirstOrDefault(registration =>
                string.Equals(registration.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

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
