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

    private sealed class RecordingProvisioner(string providerId) : IResourceIdentityProvisioner
    {
        public List<ResourceIdentityProvisioningRequest> Requests { get; } = [];

        public string ProviderId => providerId;

        public bool CanProvision(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, ProviderId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceIdentityProvisioningResult> ProvisionAsync(
            ResourceIdentityProvisioningRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ResourceIdentityProvisioningResult(request.Provider.Id));
        }
    }

    private sealed class TestCloudShellBuilder : ICloudShellBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
    }
}
