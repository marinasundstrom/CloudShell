using CloudShell.Client.Authentication;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.ControlPlane.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CloudShell.ControlPlane.Client.Tests;

public sealed class RemoteControlPlaneAuthenticationTests
{
    [Fact]
    public async Task StaticBearerCredential_CanCallProtectedControlPlane()
    {
        await using var app = await CreateProtectedAppAsync();
        var token = await GetTokenAsync(app.GetTestClient());
        var controlPlane = CreateClient(
            app,
            new StaticBearerControlPlaneCredential(token),
            CreateOptions());

        var groups = await controlPlane.ListResourceGroupsAsync();

        Assert.Empty(groups);
    }

    [Fact]
    public async Task ClientCredentialsCredential_CanCallProtectedControlPlane()
    {
        await using var app = await CreateProtectedAppAsync();
        var options = CreateOptions(credential =>
        {
            credential.Mode = "ClientCredentials";
            credential.ClientId = "cloudshell-test";
            credential.ClientSecret = "local-development-client-secret";
            credential.Scopes = ["ControlPlane.Access"];
        });
        var credential = new ClientCredentialsControlPlaneCredential(
            new TestServerHttpClientFactory(app),
            Options.Create(options));
        var controlPlane = CreateClient(app, credential, options);

        var groups = await controlPlane.ListResourceGroupsAsync();

        Assert.Empty(groups);
    }

    [Fact]
    public async Task CloudShellResourceCredential_CanCallProtectedControlPlane()
    {
        await using var app = await CreateProtectedAppAsync();
        var token = await GetTokenAsync(app.GetTestClient());
        var resourceCredential = new TestCloudShellResourceCredential(token);
        var controlPlane = CreateClient(
            app,
            new CloudShellResourceControlPlaneCredential(resourceCredential),
            CreateOptions());

        var groups = await controlPlane.ListResourceGroupsAsync();

        Assert.Empty(groups);
        Assert.Equal(["ControlPlane.Access"], resourceCredential.RequestedScopes);
    }

    [Fact]
    public async Task CloudShellResourceCredential_UsesConfiguredControlPlaneScopes()
    {
        await using var app = await CreateProtectedAppAsync();
        var token = await GetTokenAsync(app.GetTestClient());
        var resourceCredential = new TestCloudShellResourceCredential(token);
        var options = CreateOptions(credential =>
        {
            credential.Scopes = ["ControlPlane.Access", "Custom.Scope"];
        });
        var controlPlane = CreateClient(
            app,
            new CloudShellResourceControlPlaneCredential(resourceCredential),
            options);

        await controlPlane.ListResourceGroupsAsync();

        Assert.Equal(["ControlPlane.Access", "Custom.Scope"], resourceCredential.RequestedScopes);
    }

    [Fact]
    public async Task EmptyCredential_CannotCallProtectedControlPlane()
    {
        await using var app = await CreateProtectedAppAsync();
        var controlPlane = CreateClient(
            app,
            new EmptyControlPlaneCredential(),
            CreateOptions());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => controlPlane.ListResourceGroupsAsync());
    }

    private static RemoteControlPlane CreateClient(
        WebApplication app,
        ControlPlaneCredential credential,
        RemoteControlPlaneOptions options)
    {
        var handler = new ControlPlaneAuthenticationHandler(
            credential,
            Options.Create(options))
        {
            InnerHandler = app.GetTestServer().CreateHandler()
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = options.BaseAddress
        };
        return new RemoteControlPlane(client);
    }

    private static RemoteControlPlaneOptions CreateOptions(
        Action<RemoteControlPlaneCredentialOptions>? configureCredential = null)
    {
        var options = new RemoteControlPlaneOptions
        {
            BaseAddress = new Uri("http://localhost")
        };
        configureCredential?.Invoke(options.Credential);
        return options;
    }

    private static async Task<WebApplication> CreateProtectedAppAsync()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Enabled"] = "true",
            ["Authentication:Mode"] = "Secret",
            ["Authentication:Secret"] = "local-development-dashboard-secret",
            ["Authentication:BuiltInAuthority:Enabled"] = "true",
            ["Authentication:BuiltInAuthority:Issuer"] = "http://localhost",
            ["Authentication:BuiltInAuthority:Audience"] = "cloudshell-control-plane",
            ["Authentication:BuiltInAuthority:Clients:cloudshell-test:Secret"] =
                "local-development-client-secret",
            ["Authentication:BuiltInAuthority:Clients:cloudshell-test:Scopes:0"] =
                "ControlPlane.Access",
            ["Authentication:BuiltInAuthority:Clients:cloudshell-test:Roles:0"] =
                "CloudShell.Administrator",
            ["Persistence:Provider"] = "Sqlite",
            ["Persistence:ConnectionString"] = "Data Source=Data/cloudshell-client-auth.db",
            ["Persistence:IdentityConnectionString"] = "Data Source=Data/identity-client-auth.db"
        });

        builder.AddCloudShellControlPlane();
        var app = builder.Build();
        await app.UseCloudShellControlPlaneAsync();
        app.MapCloudShellControlPlane();
        await app.StartAsync();
        return app;
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/api/auth/v1/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "cloudshell-test",
                ["client_secret"] = "local-development-client-secret",
                ["scope"] = "ControlPlane.Access"
            }));
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return token?.AccessToken ??
            throw new InvalidOperationException("The token endpoint returned no access token.");
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed class TestCloudShellResourceCredential(string token) : CloudShellResourceCredential
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

    private sealed class TestServerHttpClientFactory(WebApplication app) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = app.GetTestClient();
            client.BaseAddress = new Uri("http://localhost");
            return client;
        }
    }
}
