using CloudShell.Client.Authentication;
using CloudShell.RabbitMQ.Client;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;

public sealed class CloudShellRabbitMQClientTests
{
    [Fact]
    public async Task CredentialResolver_SendsBearerTokenAndCredentialRequest()
    {
        var handler = new RecordingHandler("""
            {
              "username": "cloudshell_api",
              "password": "short-lived",
              "virtualHost": "cloudshell_sample",
              "expiresOn": "2026-06-21T12:00:00Z"
            }
            """);
        var credential = new RecordingCredential("rabbit-token");
        var resolver = new CloudShellRabbitMQCredentialResolver(
            new Uri("http://localhost/api/rabbitmq/v1/credentials"),
            credential,
            new HttpClient(handler));

        var resolved = await resolver.ResolveCredentialAsync(
            new CloudShellRabbitMQCredentialRequest(
                "application.rabbitmq:rabbitmq",
                CloudShellRabbitMQPermissions.Publish));

        Assert.Equal("cloudshell_api", resolved.Username);
        Assert.Equal("short-lived", resolved.Password);
        Assert.Equal("cloudshell_sample", resolved.VirtualHost);
        Assert.Equal(DateTimeOffset.Parse("2026-06-21T12:00:00Z"), resolved.ExpiresOn);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("rabbit-token", handler.Requests[0].Headers.Authorization?.Parameter);
        Assert.Equal([CloudShellRabbitMQPermissions.Publish], credential.RequestedScopes);

        using var payload = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal(
            "application.rabbitmq:rabbitmq",
            payload.RootElement.GetProperty("rabbitMQResourceName").GetString());
        Assert.Equal(
            CloudShellRabbitMQPermissions.Publish,
            payload.RootElement.GetProperty("permission").GetString());
    }

    [Fact]
    public async Task CredentialResolver_UsesConfiguredScopesWhenProvided()
    {
        var handler = new RecordingHandler("""
            {
              "username": "cloudshell_api",
              "password": "short-lived",
              "virtualHost": "cloudshell_sample"
            }
            """);
        var credential = new RecordingCredential("rabbit-token");
        var resolver = new CloudShellRabbitMQCredentialResolver(
            new Uri("http://localhost/api/rabbitmq/v1/credentials"),
            credential,
            new HttpClient(handler),
            ["ControlPlane.Access"]);

        await resolver.ResolveCredentialAsync(
            new CloudShellRabbitMQCredentialRequest(
                "application.rabbitmq:rabbitmq",
                CloudShellRabbitMQPermissions.Consume));

        Assert.Equal(["ControlPlane.Access"], credential.RequestedScopes);
    }

    [Fact]
    public async Task CredentialResolver_DefaultsToConfigurePermission()
    {
        var handler = new RecordingHandler("""
            {
              "username": "cloudshell_api",
              "password": "short-lived",
              "virtualHost": "cloudshell_sample"
            }
            """);
        var credential = new RecordingCredential("rabbit-token");
        var resolver = new CloudShellRabbitMQCredentialResolver(
            new Uri("http://localhost/api/rabbitmq/v1/credentials"),
            credential,
            new HttpClient(handler));

        await resolver.ResolveCredentialAsync(
            new CloudShellRabbitMQCredentialRequest("application.rabbitmq:rabbitmq"));

        Assert.Equal([CloudShellRabbitMQPermissions.Configure], credential.RequestedScopes);

        using var payload = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal(
            CloudShellRabbitMQPermissions.Configure,
            payload.RootElement.GetProperty("permission").GetString());
    }

    [Fact]
    public async Task CredentialResolver_ThrowsWhenEndpointRejectsRequest()
    {
        var handler = new RecordingHandler(
            """{ "title": "Forbidden" }""",
            HttpStatusCode.Forbidden);
        var resolver = new CloudShellRabbitMQCredentialResolver(
            new Uri("http://localhost/api/rabbitmq/v1/credentials"),
            new RecordingCredential("rabbit-token"),
            new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await resolver.ResolveCredentialAsync(
                new CloudShellRabbitMQCredentialRequest(
                    "application.rabbitmq:rabbitmq",
                    CloudShellRabbitMQPermissions.Publish)));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task CreateConnectionFactoryAsync_UsesResolvedCredentials()
    {
        var resolver = new CapturingResolver(new CloudShellRabbitMQCredential(
            "cloudshell_api",
            "short-lived-password",
            "cloudshell_sample"));
        var factory = new CloudShellRabbitMQConnectionFactory(resolver);

        var connectionFactory = await factory.CreateConnectionFactoryAsync(
            new CloudShellRabbitMQConnectionRequest(
                "application.rabbitmq:rabbitmq",
                "localhost",
                5678,
                CloudShellRabbitMQPermissions.Publish,
                "sample-client"));

        Assert.Equal("localhost", connectionFactory.HostName);
        Assert.Equal(5678, connectionFactory.Port);
        Assert.Equal("cloudshell_api", connectionFactory.UserName);
        Assert.Equal("short-lived-password", connectionFactory.Password);
        Assert.Equal("cloudshell_sample", connectionFactory.VirtualHost);
        Assert.Equal("application.rabbitmq:rabbitmq", resolver.Request?.RabbitMQResourceName);
        Assert.Equal(CloudShellRabbitMQPermissions.Publish, resolver.Request?.Permission);
    }

    [Fact]
    public void AddCloudShellRabbitMQClient_RegistersResolverAndConnectionFactory()
    {
        var services = new ServiceCollection();

        services.AddCloudShellRabbitMQClient(options =>
        {
            options.CredentialEndpoint = new Uri("http://localhost/api/rabbitmq/v1/credentials");
            options.Credential = new RecordingCredential("rabbit-token");
            options.RabbitMQResourceName = "application.rabbitmq:rabbitmq";
            options.HostName = "localhost";
            options.Port = 5678;
        });

        using var serviceProvider = services.BuildServiceProvider();

        var resolver = Assert.IsType<CloudShellRabbitMQCredentialResolver>(
            serviceProvider.GetRequiredService<ICloudShellRabbitMQCredentialResolver>());
        Assert.Equal(
            new Uri("http://localhost/api/rabbitmq/v1/credentials"),
            resolver.CredentialEndpoint);
        Assert.NotNull(serviceProvider.GetRequiredService<CloudShellRabbitMQConnectionFactory>());
    }

    [Theory]
    [InlineData("", "localhost", 5672)]
    [InlineData("application.rabbitmq:rabbitmq", "", 5672)]
    [InlineData("application.rabbitmq:rabbitmq", "localhost", 0)]
    public void ConnectionRequest_RejectsRequiredValues(
        string rabbitMQResourceName,
        string hostName,
        int port)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new CloudShellRabbitMQConnectionRequest(
                rabbitMQResourceName,
                hostName,
                port));
    }

    private sealed class CapturingResolver(CloudShellRabbitMQCredential credential) :
        ICloudShellRabbitMQCredentialResolver
    {
        public CloudShellRabbitMQCredentialRequest? Request { get; private set; }

        public ValueTask<CloudShellRabbitMQCredential> ResolveCredentialAsync(
            CloudShellRabbitMQCredentialRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(credential);
        }
    }

    private sealed class RecordingCredential(string token) : CloudShellResourceCredential
    {
        public IReadOnlyList<string> RequestedScopes { get; private set; } = [];

        public override ValueTask<CloudShellResourceAccessToken> GetTokenAsync(
            CloudShellResourceTokenRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestedScopes = request.Scopes.ToArray();
            return ValueTask.FromResult(
                new CloudShellResourceAccessToken(
                    token,
                    DateTimeOffset.UtcNow.AddMinutes(5)));
        }
    }

    private sealed class RecordingHandler(
        string responseJson,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    responseJson,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
