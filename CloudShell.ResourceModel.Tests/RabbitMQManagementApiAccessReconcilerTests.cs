using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ResourceModelResource = CloudShell.ResourceModel.Resource;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceManagerResourceState = CloudShell.Abstractions.ResourceManager.ResourceState;

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

    [Fact]
    public async Task ReconcileAccessAsync_DoesNotProjectMissingBrokerPermissions()
    {
        var handler = new RecordingHttpMessageHandler();
        var reconciler = CreateReconciler(
            handler,
            new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"));
        var resource = CreateRabbitMQResource(withManagementEndpoint: true);
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

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
        var applyPermissions = handler.Requests[1];
        Assert.Equal(
            "http://localhost:15672/api/permissions/%2F/cloudshell-api",
            applyPermissions.Uri);
        using var permissionsDocument = JsonDocument.Parse(applyPermissions.Body);
        Assert.Equal(
            string.Empty,
            permissionsDocument.RootElement.GetProperty("configure").GetString());
        Assert.Equal(
            ".*",
            permissionsDocument.RootElement.GetProperty("write").GetString());
        Assert.Equal(
            string.Empty,
            permissionsDocument.RootElement.GetProperty("read").GetString());
    }

    [Fact]
    public async Task ReconcileAccessAsync_DoesNotCreateBrokerUserWhenNoBrokerGrantsExist()
    {
        var handler = new RecordingHttpMessageHandler();
        var reconciler = CreateReconciler(
            handler,
            new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"));
        var resource = CreateRabbitMQResource(withManagementEndpoint: true);

        var diagnostics = await reconciler.ReconcileAccessAsync(
            resource,
            []);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == "application.rabbitmq.noBrokerAccessGrants");
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReconcileAccessAsync_UsesResourceVirtualHost()
    {
        var handler = new RecordingHttpMessageHandler();
        var reconciler = CreateReconciler(
            handler,
            new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"));
        var resource = CreateRabbitMQResource(
            withManagementEndpoint: true,
            builder => builder.WithVirtualHost("my_vhost"));
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

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
        Assert.Equal(
            "http://localhost:15672/api/permissions/my_vhost/cloudshell-api",
            handler.Requests[1].Uri);
    }

    [Fact]
    public async Task ManagementApiStatusProvider_ReportsAppliedWhenBrokerPermissionExists()
    {
        var handler = new RecordingHttpMessageHandler
        {
            OnSend = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new RabbitMQObservedPermissions(
                    Configure: string.Empty,
                    Write: ".*",
                    Read: string.Empty))
            }
        };
        var provider = CreateStatusProvider(
            handler,
            new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"));
        var resource = CreateResourceManagerRabbitMQResource(withManagementEndpoint: true);
        var grant = CreateGrant(resource, RabbitMQResourceOperationPermissions.Publish);

        var status = await provider.GetStatusAsync(
            new ResourcePermissionGrantStatusRequest(resource, grant));

        Assert.Equal(ResourcePermissionGrantEffectivenessState.Applied, status.State);
        Assert.Equal(RabbitMQResourceTypeProvider.ProviderId, status.ProviderId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "http://localhost:15672/api/permissions/%2F/cloudshell-api",
            request.Uri);
    }

    [Fact]
    public async Task ManagementApiStatusProvider_UsesResourceVirtualHost()
    {
        var handler = new RecordingHttpMessageHandler
        {
            OnSend = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new RabbitMQObservedPermissions(
                    Configure: string.Empty,
                    Write: ".*",
                    Read: string.Empty))
            }
        };
        var provider = CreateStatusProvider(
            handler,
            new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"));
        var resource = CreateResourceManagerRabbitMQResource(
            withManagementEndpoint: true,
            attributes: new Dictionary<string, string>
            {
                [RabbitMQResourceTypeProvider.Attributes.VirtualHost.ToString()] = "my_vhost"
            });
        var grant = CreateGrant(resource, RabbitMQResourceOperationPermissions.Publish);

        var status = await provider.GetStatusAsync(
            new ResourcePermissionGrantStatusRequest(resource, grant));

        Assert.Equal(ResourcePermissionGrantEffectivenessState.Applied, status.State);
        Assert.Equal(
            "http://localhost:15672/api/permissions/my_vhost/cloudshell-api",
            Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task ManagementApiStatusProvider_ReportsDriftedWhenBrokerPermissionIsMissing()
    {
        var handler = new RecordingHttpMessageHandler
        {
            OnSend = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new RabbitMQObservedPermissions(
                    Configure: string.Empty,
                    Write: string.Empty,
                    Read: ".*"))
            }
        };
        var provider = CreateStatusProvider(
            handler,
            new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"));
        var resource = CreateResourceManagerRabbitMQResource(withManagementEndpoint: true);
        var grant = CreateGrant(resource, RabbitMQResourceOperationPermissions.Publish);

        var status = await provider.GetStatusAsync(
            new ResourcePermissionGrantStatusRequest(resource, grant));

        Assert.Equal(ResourcePermissionGrantEffectivenessState.Drifted, status.State);
    }

    [Fact]
    public async Task ManagementApiStatusProvider_ReportsNotAppliedWhenBrokerPermissionDoesNotExist()
    {
        var handler = new RecordingHttpMessageHandler
        {
            OnSend = request => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
        var provider = CreateStatusProvider(
            handler,
            new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"));
        var resource = CreateResourceManagerRabbitMQResource(withManagementEndpoint: true);
        var grant = CreateGrant(resource, RabbitMQResourceOperationPermissions.Consume);

        var status = await provider.GetStatusAsync(
            new ResourcePermissionGrantStatusRequest(resource, grant));

        Assert.Equal(ResourcePermissionGrantEffectivenessState.NotApplied, status.State);
    }

    [Fact]
    public async Task RabbitMQPermissionGrantStatusProvider_DelegatesBrokerGrantStatusWhenInspectorIsRegistered()
    {
        var handler = new RecordingHttpMessageHandler
        {
            OnSend = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new RabbitMQObservedPermissions(
                    Configure: string.Empty,
                    Write: string.Empty,
                    Read: ".*"))
            }
        };
        var statusProvider = new RabbitMQPermissionGrantStatusProvider(
            [
                CreateStatusProvider(
                    handler,
                    new FixedRabbitMQPrincipalCredentialProvider("cloudshell-api", "broker-secret"))
            ]);
        var resource = CreateResourceManagerRabbitMQResource(withManagementEndpoint: true);
        var grant = CreateGrant(resource, RabbitMQResourceOperationPermissions.Consume);

        var status = await statusProvider.GetStatusAsync(
            new ResourcePermissionGrantStatusRequest(resource, grant));

        Assert.Equal(ResourcePermissionGrantEffectivenessState.Applied, status.State);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RabbitMQPermissionGrantStatusProvider_ReportsReconcileAccessAsCloudShellApplied()
    {
        var statusProvider = new RabbitMQPermissionGrantStatusProvider();
        var resource = CreateResourceManagerRabbitMQResource(withManagementEndpoint: false);
        var grant = CreateGrant(resource, RabbitMQResourceOperationPermissions.ReconcileAccess);

        var status = await statusProvider.GetStatusAsync(
            new ResourcePermissionGrantStatusRequest(resource, grant));

        Assert.Equal(ResourcePermissionGrantEffectivenessState.Applied, status.State);
    }

    [Fact]
    public async Task ManagementApiTopologyProvider_ReadsQueuesAndExchanges()
    {
        var handler = new RecordingHttpMessageHandler
        {
            OnSend = request =>
            {
                var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
                if (uri.Contains("/api/queues/", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(
                        """
                        [
                          {
                            "name": "rabbitmq-dotnet-events",
                            "vhost": "/",
                            "durable": true,
                            "auto_delete": false,
                            "exclusive": false,
                            "state": "running",
                            "messages": 3,
                            "consumers": 1,
                            "type": "classic"
                          }
                        ]
                        """);
                }

                if (uri.Contains("/api/bindings/", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(
                        """
                        [
                          {
                            "source": "cloudshell.sample.events",
                            "vhost": "/",
                            "destination": "rabbitmq-dotnet-events",
                            "destination_type": "queue",
                            "routing_key": "",
                            "properties_key": "~"
                          }
                        ]
                        """);
                }

                return JsonResponse(
                    """
                    [
                      {
                        "name": "",
                        "vhost": "/",
                        "type": "direct",
                        "durable": true,
                        "auto_delete": false,
                        "internal": false
                      },
                      {
                        "name": "cloudshell.sample.events",
                        "vhost": "/",
                        "type": "fanout",
                        "durable": true,
                        "auto_delete": false,
                        "internal": false
                      }
                    ]
                    """);
            }
        };
        var provider = CreateTopologyProvider(handler);
        var resource = CreateResourceManagerRabbitMQResource(withManagementEndpoint: true);

        var topology = await provider.GetTopologyAsync(resource);

        Assert.True(topology.Succeeded, topology.ErrorMessage);
        Assert.Equal("/", topology.VirtualHost);
        var queue = Assert.Single(topology.Queues);
        Assert.Equal("rabbitmq-dotnet-events", queue.Name);
        Assert.Equal("classic", queue.Type);
        Assert.Equal("running", queue.State);
        Assert.Equal(3, queue.Messages);
        Assert.Equal(1, queue.Consumers);
        Assert.True(queue.Durable);
        Assert.False(queue.AutoDelete);
        Assert.False(queue.Exclusive);

        Assert.Equal(2, topology.Exchanges.Count);
        Assert.Equal(string.Empty, topology.Exchanges[0].Name);
        Assert.Equal("cloudshell.sample.events", topology.Exchanges[1].Name);
        Assert.Equal("fanout", topology.Exchanges[1].Type);

        var binding = Assert.Single(topology.Bindings);
        Assert.Equal("cloudshell.sample.events", binding.Source);
        Assert.Equal("/", binding.VirtualHost);
        Assert.Equal("rabbitmq-dotnet-events", binding.Destination);
        Assert.Equal("queue", binding.DestinationType);
        Assert.Equal(string.Empty, binding.RoutingKey);
        Assert.Equal("~", binding.PropertiesKey);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(
            "http://localhost:15672/api/queues/%2F?disable_stats=true",
            handler.Requests[0].Uri);
        Assert.Equal(
            "http://localhost:15672/api/exchanges/%2F?disable_stats=true",
            handler.Requests[1].Uri);
        Assert.Equal(
            "http://localhost:15672/api/bindings/%2F",
            handler.Requests[2].Uri);
        var expectedAuthorization = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("admin:admin-secret"));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Basic", request.Authorization?.Scheme);
            Assert.Equal(expectedAuthorization, request.Authorization?.Parameter);
        });
    }

    [Fact]
    public async Task ResourceManagerProjection_DoesNotExposeRabbitMQPasswordAttribute()
    {
        var resource = CreateRabbitMQResource(
            withManagementEndpoint: true,
            builder => builder
                .WithUser("developer", "local-password")
                .WithVirtualHost("my_vhost"));

        var projected = ResourceModelResourceManagerMapper.ToResourceManagerResource(resource);

        Assert.True(projected.ResourceAttributes.ContainsKey(
            RabbitMQResourceTypeProvider.Attributes.UserName.ToString()));
        Assert.True(projected.ResourceAttributes.ContainsKey(
            RabbitMQResourceTypeProvider.Attributes.VirtualHost.ToString()));
        Assert.False(projected.ResourceAttributes.ContainsKey(
            RabbitMQResourceTypeProvider.Attributes.UserPassword.ToString()));
    }

    [Fact]
    public async Task ManagementApiTopologyProvider_ReportsUnavailableWhenManagementEndpointIsMissing()
    {
        var handler = new RecordingHttpMessageHandler();
        var provider = CreateTopologyProvider(handler);
        var resource = CreateResourceManagerRabbitMQResource(withManagementEndpoint: false);

        var topology = await provider.GetTopologyAsync(resource);

        Assert.False(topology.Succeeded);
        Assert.Equal("/", topology.VirtualHost);
        Assert.Contains("resolved management endpoint", topology.ErrorMessage);
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

    private static RabbitMQManagementApiPermissionGrantEffectivenessProvider CreateStatusProvider(
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
        return new RabbitMQManagementApiPermissionGrantEffectivenessProvider(
            new SingleHttpClientFactory(new HttpClient(handler)),
            configuration,
            Options.Create(new RabbitMQManagementAccessOptions()),
            credentialProvider);
    }

    private static RabbitMQManagementApiBrokerTopologyProvider CreateTopologyProvider(
        RecordingHttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RabbitMQResourceDefaults.UsernameConfigurationKey] = "admin",
                [RabbitMQResourceDefaults.PasswordConfigurationKey] = "admin-secret"
            })
            .Build();
        return new RabbitMQManagementApiBrokerTopologyProvider(
            new SingleHttpClientFactory(new HttpClient(handler)),
            configuration,
            Options.Create(new RabbitMQManagementAccessOptions()));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };

    private static ResourceModelResource CreateRabbitMQResource(
        bool withManagementEndpoint,
        Action<RabbitMQResourceDefinitionBuilder>? configure = null)
    {
        var graph = new ResourceGraphBuilder();
        var builder = graph
            .AddRabbitMQ("rabbitmq")
            .WithAmqpEndpoint(host: "localhost", port: 5672);
        if (withManagementEndpoint)
        {
            builder.WithManagementEndpoint(host: "localhost", port: 15672);
        }

        configure?.Invoke(builder);

        var definition = graph.BuildGraph().Resources.Single(resource =>
            resource.TypeId == RabbitMQResourceTypeProvider.ResourceTypeId);
        var resolver = new ResourceResolver(
            [RabbitMQResourceTypeProvider.ClassDefinition],
            [new RabbitMQResourceTypeProvider().TypeDefinition]);
        return resolver.Resolve(definition);
    }

    private static ResourceManagerResource CreateResourceManagerRabbitMQResource(
        bool withManagementEndpoint,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        var endpointMappings = withManagementEndpoint
            ?
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    "application.rabbitmq:rabbitmq",
                    "management",
                    "http://localhost:15672")
            ]
            : Array.Empty<ResourceEndpointNetworkMapping>();
        return new ResourceManagerResource(
            "application.rabbitmq:rabbitmq",
            "rabbitmq",
            RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
            RabbitMQResourceTypeProvider.ProviderId,
            "local",
            ResourceManagerResourceState.Unknown,
            [
                new ResourceEndpoint(
                    "management",
                    "http",
                    ResourceExposureScope.Local,
                    15672)
            ],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
            Attributes: attributes,
            EndpointNetworkMappings: endpointMappings);
    }

    private static ResourcePermissionGrant CreateGrant(
        ResourceManagerResource resource,
        string permission) =>
        new(
            ResourcePrincipalReference.ForResourceIdentity(
                "application.aspnet-core-project:api",
                "default"),
            resource.Id,
            permission);

    private sealed class FixedRabbitMQPrincipalCredentialProvider(
        string userName,
        string password) : IRabbitMQPrincipalCredentialProvider
    {
        public RabbitMQPrincipalCredentials CreateCredentials(
            string targetResourceId,
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

        public Func<HttpRequestMessage, HttpResponseMessage>? OnSend { get; set; }

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
            return OnSend?.Invoke(request) ??
                new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        AuthenticationHeaderValue? Authorization,
        string Body);
}
