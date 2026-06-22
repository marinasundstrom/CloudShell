using CloudShell.Client.Authentication;
using CloudShell.SqlServer.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;

public sealed class CloudShellSqlConnectionFactoryTests
{
    [Fact]
    public async Task CredentialResolver_SendsBearerTokenAndConnectionRequest()
    {
        var handler = new RecordingHandler("""
            {
              "connectionString": "Server=localhost,14334;Database=application_topology;User Id=cloudshell_api;Password=short-lived;Encrypt=False;Trust Server Certificate=True",
              "expiresOn": "2026-06-21T12:00:00Z"
            }
            """);
        var credential = new RecordingCredential("sql-token");
        var resolver = new CloudShellSqlCredentialResolver(
            new Uri("http://localhost/api/sql-server/v1/credentials"),
            credential,
            new HttpClient(handler),
            ["ControlPlane.Access"]);

        var resolved = await resolver.ResolveCredentialAsync(
            new CloudShellSqlConnectionRequest(
                "application-topology-sql-server",
                "application_topology",
                "Database/databases/readWrite/action"));

        Assert.Contains("User Id=cloudshell_api", resolved.ConnectionString);
        Assert.Equal(DateTimeOffset.Parse("2026-06-21T12:00:00Z"), resolved.ExpiresOn);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("sql-token", handler.Requests[0].Headers.Authorization?.Parameter);
        Assert.Equal(["ControlPlane.Access"], credential.RequestedScopes);

        using var payload = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal(
            "application-topology-sql-server",
            payload.RootElement.GetProperty("sqlServerResourceName").GetString());
        Assert.Equal(
            "application_topology",
            payload.RootElement.GetProperty("databaseName").GetString());
        Assert.Equal(
            "Database/databases/readWrite/action",
            payload.RootElement.GetProperty("permission").GetString());
    }

    [Fact]
    public async Task CredentialResolver_ThrowsWhenEndpointRejectsRequest()
    {
        var handler = new RecordingHandler(
            """{ "title": "Forbidden" }""",
            HttpStatusCode.Forbidden);
        var resolver = new CloudShellSqlCredentialResolver(
            new Uri("http://localhost/api/sql-server/v1/credentials"),
            new RecordingCredential("sql-token"),
            new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await resolver.ResolveCredentialAsync(
                new CloudShellSqlConnectionRequest(
                    "application-topology-sql-server",
                    "application_topology")));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task CreateConnectionAsync_UsesResolvedConnectionString()
    {
        var resolver = new CapturingResolver(new CloudShellSqlCredential(
            new SqlConnectionStringBuilder
            {
                DataSource = "localhost,14334",
                InitialCatalog = "application_topology",
                UserID = "cloudshell_api",
                Password = "short-lived-password",
                Encrypt = false,
                TrustServerCertificate = true
            }.ConnectionString));
        var factory = new CloudShellSqlConnectionFactory(resolver);

        await using var connection = await factory.CreateConnectionAsync(
            "application-topology-sql-server",
            "application_topology");

        Assert.Equal("localhost,14334", connection.DataSource);
        Assert.Equal("application_topology", connection.Database);
        Assert.Equal("application-topology-sql-server", resolver.Request?.SqlServerResourceName);
        Assert.Equal("application_topology", resolver.Request?.DatabaseName);
    }

    [Fact]
    public async Task CreateConnectionAsync_RejectsEmptyResolvedConnectionString()
    {
        var resolver = new CapturingResolver(new CloudShellSqlCredential(""));
        var factory = new CloudShellSqlConnectionFactory(resolver);

        var exception = await Assert.ThrowsAsync<CloudShellSqlCredentialException>(
            async () => await factory.CreateConnectionAsync(
                "application-topology-sql-server",
                "application_topology"));

        Assert.Contains("returned no connection string", exception.Message);
    }

    [Fact]
    public void AddCloudShellSqlServerClient_RegistersResolverAndConnectionFactory()
    {
        var services = new ServiceCollection();

        services.AddCloudShellSqlServerClient(options =>
        {
            options.CredentialEndpoint = new Uri("http://localhost/api/sql-server/v1/credentials");
            options.Credential = new RecordingCredential("sql-token");
        });

        using var serviceProvider = services.BuildServiceProvider();

        var resolver = Assert.IsType<CloudShellSqlCredentialResolver>(
            serviceProvider.GetRequiredService<ICloudShellSqlCredentialResolver>());
        Assert.Equal(
            new Uri("http://localhost/api/sql-server/v1/credentials"),
            resolver.CredentialEndpoint);
        Assert.NotNull(serviceProvider.GetRequiredService<CloudShellSqlConnectionFactory>());
    }

    [Theory]
    [InlineData("", "application_topology")]
    [InlineData("application-topology-sql-server", "")]
    public void ConnectionRequest_RejectsRequiredValues(
        string sqlServerResourceName,
        string databaseName)
    {
        Assert.Throws<ArgumentException>(
            () => new CloudShellSqlConnectionRequest(sqlServerResourceName, databaseName));
    }

    private sealed class CapturingResolver(CloudShellSqlCredential credential) :
        ICloudShellSqlCredentialResolver
    {
        public CloudShellSqlConnectionRequest? Request { get; private set; }

        public ValueTask<CloudShellSqlCredential> ResolveCredentialAsync(
            CloudShellSqlConnectionRequest request,
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
