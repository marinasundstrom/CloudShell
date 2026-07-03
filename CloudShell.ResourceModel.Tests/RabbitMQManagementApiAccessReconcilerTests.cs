using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ResourceModel.Tests;

public sealed class RabbitMQManagementApiAccessReconcilerTests
{
    [Fact]
    public async Task ReconcileAccessAsync_AppliesResourceIdentityGrantsThroughManagementApi()
    {
        var handler = new RecordingHttpMessageHandler();
        var reconciler = CreateReconciler(
            handler,
            new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"));
        var resource = CreateRabbitMQResource(withManagementEndpoint: true);
        var principal = ResourcePrincipalReference.ForResourceIdentity(
            "application.aspnet-core-project:api",
            "default");
        var grants = new[]
        {
            new ResourcePermissionGrant(
                principal,
                resource.EffectiveResourceId,
                RabbitMQResourceOperationPermissions.Publish),
            new ResourcePermissionGrant(
                principal,
                resource.EffectiveResourceId,
                RabbitMQResourceOperationPermissions.Consume),
            new ResourcePermissionGrant(
                principal,
                resource.EffectiveResourceId,
                RabbitMQResourceOperationPermissions.Configure),
            new ResourcePermissionGrant(
                principal,
                resource.EffectiveResourceId,
                RabbitMQResourceOperationPermissions.ReconcileAccess)
        };

        var diagnostics = await reconciler.ReconcileAccessAsync(resource, grants);

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == "application.rabbitmq.accessReconciled");
        Assert.Equal(2, handler.Requests.Count);
        var expectedAuthorization = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("admin:admin-secret"));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Basic", request.Authorization?.Scheme);
            Assert.Equal(expectedAuthorization, request.Authorization?.Parameter);
        });

        var createUser = handler.Requests[0];
        Assert.Equal(HttpMethod.Put, createUser.Method);
        Assert.Equal(
            "http://localhost:15672/api/users/cloudshell-api",
            createUser.Uri);
        using (var userDocument = JsonDocument.Parse(createUser.Body))
        {
            Assert.Equal(
                "broker-secret",
                userDocument.RootElement.GetProperty("password").GetString());
            Assert.Equal(
                string.Empty,
                userDocument.RootElement.GetProperty("tags").GetString());
        }

        var applyPermissions = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, applyPermissions.Method);
        Assert.Equal(
            "http://localhost:15672/api/permissions/%2F/cloudshell-api",
            applyPermissions.Uri);
        using var permissionsDocument = JsonDocument.Parse(applyPermissions.Body);
        Assert.Equal(
            ".*",
            permissionsDocument.RootElement.GetProperty("configure").GetString());
        Assert.Equal(
            ".*",
            permissionsDocument.RootElement.GetProperty("write").GetString());
        Assert.Equal(
            ".*",
            permissionsDocument.RootElement.GetProperty("read").GetString());
    }

    [Fact]
    public async Task ReconcileAccessAsync_ReportsDiagnosticWhenManagementEndpointIsMissing()
    {
        var handler = new RecordingHttpMessageHandler();
        var reconciler = CreateReconciler(
            handler,
            new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"));
        var resource = CreateRabbitMQResource(withManagementEndpoint: false);
        var principal = ResourcePrincipalReference.ForResourceIdentity(
            "application.aspnet-core-project:api",
            "default");

        var diagnostics = await reconciler.ReconcileAccessAsync(
            resource,
            [
                new ResourcePermissionGrant(
                    principal,
                    resource.EffectiveResourceId,
                    RabbitMQResourceOperationPermissions.Publish)
            ]);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("application.rabbitmq.managementEndpointRequired", diagnostic.Code);
        Assert.Empty(handler.Requests);
    }

    private static RabbitMQManagementApiAccessReconciler CreateReconciler(
        RecordingHttpMessageHandler handler,
        IRabbitMQPrincipalCredentialProvider credentialProvider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RabbitMQResourceDefaults.UsernameConfigurationKey] = "admin",
                [RabbitMQResourceDefaults.PasswordConfigurationKey] = "admin-secret"
            })
            .Build();
        return new RabbitMQManagementApiAccessReconciler(
            new SingleHttpClientFactory(new HttpClient(handler)),
            configuration,
            Options.Create(new RabbitMQManagementAccessOptions()),
            credentialProvider);
    }

    private static ResourceModelResource CreateRabbitMQResource(
        bool withManagementEndpoint)
    {
        var graph = new ResourceGraphBuilder();
        var builder = graph
            .AddRabbitMQ("rabbitmq")
            .WithAmqpEndpoint(host: "localhost", port: 5672);
        if (withManagementEndpoint)
        {
            builder.WithManagementEndpoint(host: "localhost", port: 15672);
        }

        var definition = graph.BuildGraph().Resources.Single(resource =>
            resource.TypeId == RabbitMQResourceTypeProvider.ResourceTypeId);
        var resolver = new ResourceResolver(
            [RabbitMQResourceTypeProvider.ClassDefinition],
            [new RabbitMQResourceTypeProvider().TypeDefinition]);
        return resolver.Resolve(definition);
    }

    private sealed class FixedRabbitMQPrincipalCredentialProvider(
        string userName,
        string password) : IRabbitMQPrincipalCredentialProvider
    {
        public RabbitMQPrincipalCredentials CreateCredentials(
            ResourceModelResource resource,
            ResourcePrincipalReference principal) =>
            new(userName, password);
    }

    private sealed class SingleHttpClientFactory(
        HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                request.Headers.Authorization,
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        AuthenticationHeaderValue? Authorization,
        string Body);
}
