using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task ControlPlaneHost_ConfiguresResourceIdentityProviderCatalog()
    {
        await using var app = await CreateAppAsync();
        var catalog = app.Services.GetRequiredService<ResourceIdentityProviderCatalog>();

        Assert.Equal("identity:entra", catalog.DefaultProviderId);
        var resolution = catalog.Resolve(ResourceIdentityBinding.RequireIdentity(["api.read"]));
        Assert.True(resolution.IsResolved);
        Assert.Equal("identity:entra", resolution.Provider?.Id);
        Assert.Equal(ResourceIdentityProviderKind.Oidc, resolution.Provider?.Kind);
        Assert.Equal(
            "https://login.microsoftonline.com/common/v2.0",
            resolution.Provider?.ProviderSettings["authority"]);
    }

    [Fact]
    public async Task ResourceIdentityProvisioning_CreatesBuiltInClientForDeclaredIdentity()
    {
        await using var app = await CreateAppAsync(resources =>
        {
            var identityProvider = resources.AddIdentityProvider(
                "identity:development",
                "Development identity",
                ResourceIdentityProviderKind.BuiltIn,
                new Dictionary<string, string>
                {
                    [BuiltInResourceIdentityRegistry.ClientSecretSettingName] =
                        "local-development-resource-secret"
                },
                useAsDefault: true);
            var api = resources
                .Declare(ResourceIdentityFlowProvider.ProviderId, ResourceIdentityFlowProvider.ApiResourceId)
                .WithIdentity(identityProvider, name: "web-api");
            var vault = resources.Declare(
                ResourceIdentityFlowProvider.ProviderId,
                ResourceIdentityFlowProvider.VaultResourceId,
                resourceClass: ResourceClass.SecretsVault);
            vault.Allow(api.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);
        });
        var client = app.GetTestClient();
        var adminToken = await GetTokenAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/control-plane/v1/resources/api/identity/provision");
        request.Headers.Authorization = new("Bearer", adminToken);
        var provisioningResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, provisioningResponse.StatusCode);
        using var provisioningDocument = JsonDocument.Parse(
            await provisioningResponse.Content.ReadAsStringAsync());
        var provisioning = provisioningDocument.RootElement;
        Assert.Equal(
            "identity:development",
            provisioning.GetProperty("providerId").GetString());
        Assert.Equal(
            (int)ResourceIdentityProvisioningDiagnosticSeverity.Information,
            provisioning.GetProperty("diagnostics")[0].GetProperty("severity").GetInt32());

        var token = await GetTokenAsync(
            client,
            "api/web-api",
            "local-development-resource-secret");
        var payload = ReadTokenPayload(token);
        Assert.Equal(
            SecretsVaultResourceOperationPermissions.ReadSecrets,
            payload.GetProperty(CloudShellAuthenticationOptions.PermissionClaimType).GetString());
        Assert.Equal(
            "secrets-vault:app",
            payload.GetProperty(CloudShellAuthenticationOptions.ResourceClaimType).GetString());
    }

    [Fact]
    public async Task ProvisionedResourceIdentityToken_RespectsResourceActionGrantsThroughApi()
    {
        await using var app = await CreateAppAsync(resources =>
        {
            var identityProvider = resources.AddIdentityProvider(
                "identity:development",
                "Development identity",
                ResourceIdentityProviderKind.BuiltIn,
                new Dictionary<string, string>
                {
                    [BuiltInResourceIdentityRegistry.ClientSecretSettingName] =
                        "local-development-resource-secret"
                },
                useAsDefault: true);
            var api = resources
                .Declare(ResourceIdentityFlowProvider.ProviderId, ResourceIdentityFlowProvider.ApiResourceId)
                .WithIdentity(identityProvider, name: "web-api");
            var allowed = resources.Declare(
                ResourceIdentityFlowProvider.ProviderId,
                ResourceIdentityFlowProvider.AllowedActionResourceId);
            var denied = resources.Declare(
                ResourceIdentityFlowProvider.ProviderId,
                ResourceIdentityFlowProvider.DeniedActionResourceId);
            api.Allow(api.Principal, CloudShellPermissions.Resources.Read);
            allowed.Allow(api.Principal, CloudShellPermissions.Resources.Read);
            allowed.Allow(api.Principal, CommonResourceOperationPermissions.LifecycleAction);
            denied.Allow(api.Principal, CloudShellPermissions.Resources.Read);
        });
        await RegisterIdentityFlowResourcesAsync(
            app,
            ResourceIdentityFlowProvider.ApiResourceId,
            ResourceIdentityFlowProvider.AllowedActionResourceId,
            ResourceIdentityFlowProvider.DeniedActionResourceId);
        var client = app.GetTestClient();
        var adminToken = await GetTokenAsync(client);
        await SendWithBearerAsync(
            client,
            HttpMethod.Post,
            "/api/control-plane/v1/resources/api/identity/provision",
            adminToken,
            HttpStatusCode.OK);
        var resourceToken = await GetTokenAsync(
            client,
            "api/web-api",
            "local-development-resource-secret");

        await SendWithBearerAsync(
            client,
            HttpMethod.Post,
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString(ResourceIdentityFlowProvider.AllowedActionResourceId)}/actions/{ResourceActionIds.Start}",
            resourceToken,
            HttpStatusCode.OK);
        await SendWithBearerAsync(
            client,
            HttpMethod.Post,
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString(ResourceIdentityFlowProvider.DeniedActionResourceId)}/actions/{ResourceActionIds.Start}",
            resourceToken,
            HttpStatusCode.Forbidden);
        await SendWithBearerAsync(
            client,
            HttpMethod.Post,
            "/api/control-plane/v1/resources/api/identity/provision",
            resourceToken,
            HttpStatusCode.Forbidden);
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

    private static async Task<WebApplication> CreateAppAsync(
        Action<IResourceGraphBuilder>? configureResources = null)
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
            ["ResourceIdentity:DefaultProviderId"] = "identity:entra",
            ["ResourceIdentity:Providers:0:Id"] = "identity:dev",
            ["ResourceIdentity:Providers:0:Name"] = "Development identity",
            ["ResourceIdentity:Providers:0:Kind"] = "Oidc",
            ["ResourceIdentity:Providers:1:Id"] = "identity:entra",
            ["ResourceIdentity:Providers:1:Name"] = "Microsoft Entra ID",
            ["ResourceIdentity:Providers:1:Kind"] = "Oidc",
            ["ResourceIdentity:Providers:1:Settings:authority"] =
                "https://login.microsoftonline.com/common/v2.0",
            ["Persistence:Provider"] = "Sqlite",
            ["Persistence:ConnectionString"] = "Data Source=Data/cloudshell-test.db",
            ["Persistence:IdentityConnectionString"] = "Data Source=Data/identity-test.db"
        });

        var controlPlane = builder.AddCloudShellControlPlane();
        builder.Services.AddSingleton<IResourceProvider, ResourceIdentityFlowProvider>();
        controlPlane.Resources(resources =>
        {
            configureResources?.Invoke(resources);
        });
        var app = builder.Build();
        await app.UseCloudShellControlPlaneAsync();
        app.MapCloudShellControlPlane();
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> RequestTokenAsync(
        HttpClient client,
        string clientId = "cloudshell-test",
        string clientSecret = "local-development-client-secret") =>
        await client.PostAsync(
            "/api/auth/v1/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "ControlPlane.Access"
            }));

    private static async Task<string> GetTokenAsync(
        HttpClient client,
        string clientId = "cloudshell-test",
        string clientSecret = "local-development-client-secret")
    {
        var response = await RequestTokenAsync(client, clientId, clientSecret);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return token?.AccessToken ??
            throw new InvalidOperationException("The token endpoint returned no access token.");
    }

    private static async Task RegisterIdentityFlowResourcesAsync(
        WebApplication app,
        params string[] resourceIds)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var registrations = scope.ServiceProvider.GetRequiredService<EfCoreResourceStore>();
        foreach (var resourceId in resourceIds)
        {
            await registrations.RegisterAsync(ResourceIdentityFlowProvider.ProviderId, resourceId);
        }
    }

    private static async Task<HttpResponseMessage> SendWithBearerAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        string accessToken,
        HttpStatusCode expectedStatusCode)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new("Bearer", accessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(expectedStatusCode, response.StatusCode);
        return response;
    }

    private static JsonElement ReadTokenPayload(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
        return document.RootElement.Clone();
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed class ResourceIdentityFlowProvider : IResourceProvider, IResourceProcedureProvider
    {
        public const string ProviderId = "identity-flow";
        public const string ApiResourceId = "api";
        public const string VaultResourceId = "secrets-vault:app";
        public const string AllowedActionResourceId = "service:allowed";
        public const string DeniedActionResourceId = "service:denied";

        public string Id => ProviderId;

        public string DisplayName => "Identity Flow";

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                ApiResourceId,
                "Web API",
                "Application",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                ResourceClass: ResourceClass.Service),
            new(
                VaultResourceId,
                "App Secrets",
                "Secrets Vault",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                ResourceClass: ResourceClass.SecretsVault),
            new(
                AllowedActionResourceId,
                "Allowed Worker",
                "Service",
                DisplayName,
                "local",
                ResourceState.Stopped,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                Actions: [ResourceAction.Start],
                ResourceClass: ResourceClass.Service),
            new(
                DeniedActionResourceId,
                "Denied Worker",
                "Service",
                DisplayName,
                "local",
                ResourceState.Stopped,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                Actions: [ResourceAction.Start],
                ResourceClass: ResourceClass.Service)
        ];

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResourceProcedureResult.Completed($"Deleted {context.Resource.Id}."));

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResourceProcedureResult.Completed(
                $"Executed {action.Id} for {context.Resource.Id}."));
    }
}
