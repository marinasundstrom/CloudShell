using System.Security.Claims;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using Microsoft.Extensions.Options;

namespace CloudShell.ResourceModel.Tests;

public sealed class RabbitMQCredentialResolverTests
{
    private const string BrokerResourceId = "application.rabbitmq:rabbitmq";
    private const string AppResourceId = "application.aspnet-core-project:api";
    private const string IdentityName = "api";
    private const string Subject = $"{AppResourceId}/{IdentityName}";

    [Fact]
    public async Task ResolveAsync_ReconcilesAccessAndReturnsCredentialsForGrantedIdentity()
    {
        var principal = ResourcePrincipalReference.ForResourceIdentity(
            AppResourceId,
            IdentityName);
        var grants = new[]
        {
            new ResourcePermissionGrant(
                principal,
                BrokerResourceId,
                RabbitMQResourceOperationPermissions.Configure),
            new ResourcePermissionGrant(
                principal,
                BrokerResourceId,
                RabbitMQResourceOperationPermissions.Publish)
        };
        var reconciler = new RecordingRabbitMQAccessReconciler();
        var events = new RecordingResourceEventSink();
        var resolver = CreateResolver(grants, reconciler, events);

        var response = await resolver.ResolveAsync(
            new ResolveRabbitMQCredentialRequest(
                BrokerResourceId,
                RabbitMQResourceOperationPermissions.Configure),
            CreatePrincipal(RabbitMQResourceOperationPermissions.Configure));

        Assert.Equal("cloudshell-api", response.Username);
        Assert.Equal("broker-secret", response.Password);
        Assert.Equal("sample_vhost", response.VirtualHost);
        var (resource, reconciledGrants) = Assert.Single(reconciler.Calls);
        Assert.Equal(BrokerResourceId, resource.EffectiveResourceId);
        Assert.Equal(2, reconciledGrants.Count);
        Assert.Contains(events.Events, resourceEvent =>
            resourceEvent.EventType == RabbitMQEvent("credential.resolved") &&
            resourceEvent.TriggeredBy == Subject);
    }

    [Fact]
    public async Task ResolveAsync_DeniesRequestWithoutResourcePermissionClaim()
    {
        var principal = ResourcePrincipalReference.ForResourceIdentity(
            AppResourceId,
            IdentityName);
        var reconciler = new RecordingRabbitMQAccessReconciler();
        var events = new RecordingResourceEventSink();
        var resolver = CreateResolver(
            [
                new ResourcePermissionGrant(
                    principal,
                    BrokerResourceId,
                    RabbitMQResourceOperationPermissions.Configure)
            ],
            reconciler,
            events);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            resolver.ResolveAsync(
                new ResolveRabbitMQCredentialRequest(
                    BrokerResourceId,
                    RabbitMQResourceOperationPermissions.Configure),
                CreatePrincipal()));

        Assert.Empty(reconciler.Calls);
        Assert.Contains(events.Events, resourceEvent =>
            resourceEvent.EventType == RabbitMQEvent("credential.request.denied") &&
            resourceEvent.Severity == ResourceSignalSeverity.Warning);
    }

    [Fact]
    public async Task ResolveAsync_DeniesRequestWithoutMatchingDeclaredGrant()
    {
        var reconciler = new RecordingRabbitMQAccessReconciler();
        var events = new RecordingResourceEventSink();
        var resolver = CreateResolver([], reconciler, events);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            resolver.ResolveAsync(
                new ResolveRabbitMQCredentialRequest(
                    BrokerResourceId,
                    RabbitMQResourceOperationPermissions.Configure),
                CreatePrincipal(RabbitMQResourceOperationPermissions.Configure)));

        Assert.Empty(reconciler.Calls);
        Assert.Contains(events.Events, resourceEvent =>
            resourceEvent.EventType == RabbitMQEvent("credential.request.denied") &&
            resourceEvent.TriggeredBy == Subject);
    }

    [Fact]
    public async Task ResolveAsync_FailsWhenAccessReconciliationFails()
    {
        var principal = ResourcePrincipalReference.ForResourceIdentity(
            AppResourceId,
            IdentityName);
        var reconciler = new RecordingRabbitMQAccessReconciler(
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.rabbitmq.accessReconciliationFailed",
                    "management API unavailable",
                    BrokerResourceId)
            ]);
        var events = new RecordingResourceEventSink();
        var resolver = CreateResolver(
            [
                new ResourcePermissionGrant(
                    principal,
                    BrokerResourceId,
                    RabbitMQResourceOperationPermissions.Configure)
            ],
            reconciler,
            events);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync(
                new ResolveRabbitMQCredentialRequest(
                    BrokerResourceId,
                    RabbitMQResourceOperationPermissions.Configure),
                CreatePrincipal(RabbitMQResourceOperationPermissions.Configure)));

        Assert.Contains("management API unavailable", exception.Message);
        Assert.Single(reconciler.Calls);
        Assert.Contains(events.Events, resourceEvent =>
            resourceEvent.EventType == RabbitMQEvent("credential.request.failed") &&
            resourceEvent.Severity == ResourceSignalSeverity.Error);
    }

    private static RabbitMQCredentialResolver CreateResolver(
        IReadOnlyList<ResourcePermissionGrant> grants,
        RecordingRabbitMQAccessReconciler reconciler,
        RecordingResourceEventSink events)
    {
        var state = CreateRabbitMQState();
        return new RabbitMQCredentialResolver(
            new ResourceGraphModel(new InMemoryResourceStateProvider([state])),
            new ResourceGraphResolver(
                new ResourceResolver(
                    [RabbitMQResourceTypeProvider.ClassDefinition],
                    [new RabbitMQResourceTypeProvider().TypeDefinition])),
            new FixedResourcePermissionGrantReader(grants),
            reconciler,
            new FixedRabbitMQPrincipalCredentialProvider(),
            Options.Create(new RabbitMQManagementAccessOptions()),
            events);
    }

    private static ResourceState CreateRabbitMQState()
    {
        var graph = new ResourceGraphBuilder();
        graph
            .AddRabbitMQ("rabbitmq")
            .WithAmqpEndpoint(host: "localhost", port: 5672)
            .WithManagementEndpoint(host: "localhost", port: 15672)
            .WithVirtualHost("sample_vhost");

        return ResourceState.FromDefinition(graph.BuildGraph().Resources.Single(resource =>
            resource.EffectiveResourceId == BrokerResourceId));
    }

    private static ClaimsPrincipal CreatePrincipal(string? permission = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Subject),
            new(ClaimTypes.Name, Subject)
        };
        if (!string.IsNullOrWhiteSpace(permission))
        {
            claims.Add(new Claim(
                CloudShellAuthorizationClaimTypes.ResourcePermission,
                ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
                    BrokerResourceId,
                    permission)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string RabbitMQEvent(string name) =>
        ResourceEventTypes.Events.Provider.ForEvent(
            RabbitMQResourceTypeProvider.ProviderId,
            name);

    private sealed class FixedResourcePermissionGrantReader(
        IReadOnlyList<ResourcePermissionGrant> grants) : IResourcePermissionGrantReader
    {
        public IReadOnlyList<ResourcePermissionGrant> GetPermissionGrants() => grants;
    }

    private sealed class RecordingRabbitMQAccessReconciler(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null) :
        IRabbitMQAccessReconciler
    {
        private readonly IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics =
            diagnostics ?? [];

        public List<(Resource Resource, IReadOnlyList<ResourcePermissionGrant> Grants)> Calls { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
            Resource resource,
            IReadOnlyList<ResourcePermissionGrant> grants,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((resource, grants));
            return ValueTask.FromResult(diagnostics);
        }
    }

    private sealed class FixedRabbitMQPrincipalCredentialProvider :
        IRabbitMQPrincipalCredentialProvider
    {
        public RabbitMQPrincipalCredentials CreateCredentials(
            string targetResourceId,
            ResourcePrincipalReference principal) =>
            new("cloudshell-api", "broker-secret");
    }

    private sealed class RecordingResourceEventSink : IResourceEventSink
    {
        public List<ResourceEvent> Events { get; } = [];

        public void Append(ResourceEvent resourceEvent) => Events.Add(resourceEvent);
    }
}
