using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CloudShell.ControlPlane.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace CloudShell.Abstractions.Tests;

public sealed class BuiltInAuthorityHttpTests
{
    [Fact]
    public async Task TokenEndpoint_IssuesClientCredentialsToken()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await RequestTokenAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.False(string.IsNullOrWhiteSpace(token?.AccessToken));
        Assert.Equal("Bearer", token.TokenType);
    }

    [Fact]
    public async Task ControlPlaneApi_RejectsMissingToken()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/control-plane/v1/resource-groups");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ControlPlaneApi_AcceptsBuiltInBearerToken()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var token = await GetTokenAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/control-plane/v1/resource-groups");
        request.Headers.Authorization = new("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ControlPlaneApi_RejectsTamperedBearerToken()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var token = await GetTokenAsync(client);
        var tampered = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/control-plane/v1/resource-groups");
        request.Headers.Authorization = new("Bearer", tampered);
        var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<WebApplication> CreateAppAsync()
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
            ["Persistence:ConnectionString"] = "Data Source=Data/cloudshell-test.db",
            ["Persistence:IdentityConnectionString"] = "Data Source=Data/identity-test.db"
        });

        builder.AddCloudShellControlPlane();
        var app = builder.Build();
        await app.UseCloudShellControlPlaneAsync();
        app.MapCloudShellControlPlane();
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> RequestTokenAsync(HttpClient client) =>
        await client.PostAsync(
            "/api/auth/v1/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "cloudshell-test",
                ["client_secret"] = "local-development-client-secret",
                ["scope"] = "ControlPlane.Access"
            }));

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var response = await RequestTokenAsync(client);
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
}
