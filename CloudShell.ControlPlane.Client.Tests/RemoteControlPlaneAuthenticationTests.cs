using CloudShell.Client.Authentication;
using CloudShell.Abstractions.Authentication;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
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
    public async Task ResourcePermissionCredential_CanExecuteScopedResourceAction()
    {
        await using var app = await CreateProtectedAppAsync(includeLifecycleResource: true);
        var token = await GetTokenAsync(
            app.GetTestClient(),
            "cloudshell-lifecycle",
            "local-development-lifecycle-secret");
        var controlPlane = CreateClient(
            app,
            new StaticBearerControlPlaneCredential(token),
            CreateOptions());

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync(
            [ContractLifecycleResourceProvider.ResourceId]);
        var result = await controlPlane.ExecuteResourceActionAsync(
            ContractLifecycleResourceProvider.ResourceId,
            ResourceActionIds.Stop);

        var capability = Assert.Single(capabilities).Value;
        Assert.False(capability.CanManage);
        Assert.False(capability.CanDelete);
        Assert.True(capability.CanExecuteAction(ResourceActionIds.Stop));
        Assert.Equal("Executed stop.", result.Message);
        var signal = Assert.Single(result.Signals);
        Assert.Equal(ResourceSignalSeverity.Warning, signal.Severity);
        Assert.Equal("Contract warning.", signal.Message);
        var provider = app.Services.GetRequiredService<ContractLifecycleResourceProvider>();
        Assert.Equal([ResourceActionIds.Stop], provider.ExecutedActions);
    }

    [Fact]
    public async Task ResourcePermissionCredential_CanInspectManagedResourceWithoutReadClaim()
    {
        await using var app = await CreateProtectedAppAsync(includeLifecycleResource: true);
        var token = await GetTokenAsync(
            app.GetTestClient(),
            "cloudshell-resource-manager",
            "local-development-resource-manager-secret");
        var controlPlane = CreateClient(
            app,
            new StaticBearerControlPlaneCredential(token),
            CreateOptions());

        var resource = await controlPlane.GetResourceAsync(ContractLifecycleResourceProvider.ResourceId);
        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync(
            [ContractLifecycleResourceProvider.ResourceId]);

        Assert.NotNull(resource);
        Assert.Equal(ContractLifecycleResourceProvider.ResourceId, resource.Id);
        var capability = Assert.Single(capabilities).Value;
        Assert.True(capability.CanManage);
        Assert.True(capability.CanDelete);
    }

    [Fact]
    public async Task IdentityUserResourcePermissionClaim_CanInspectManagedResource()
    {
        await using var app = await CreateIdentityProtectedAppAsync(includeLifecycleResource: true);
        await CreateIdentityUserAsync(
            app,
            "resource.manager@example.test",
            "CloudShell123!",
            new Claim(
                CloudShellAuthenticationOptions.ResourcePermissionClaimType,
                ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
                    ContractLifecycleResourceProvider.ResourceId,
                    CloudShellPermissions.Resources.Manage)));
        var token = await GetPasswordTokenAsync(
            app.GetTestClient(),
            "resource.manager@example.test",
            "CloudShell123!");
        var controlPlane = CreateClient(
            app,
            new StaticBearerControlPlaneCredential(token),
            CreateOptions());

        var resource = await controlPlane.GetResourceAsync(ContractLifecycleResourceProvider.ResourceId);
        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync(
            [ContractLifecycleResourceProvider.ResourceId]);

        Assert.NotNull(resource);
        Assert.Equal(ContractLifecycleResourceProvider.ResourceId, resource.Id);
        var capability = Assert.Single(capabilities).Value;
        Assert.True(capability.CanManage);
        Assert.True(capability.CanDelete);
    }

    [Fact]
    public async Task BuiltInAccountService_CreatesLocalPermissionTestUser()
    {
        await using var app = await CreateIdentityProtectedAppAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<CloudShellAccountService>();

        Assert.Equal(CloudShellLocalUserStoreKind.Persistent, accounts.LocalUserStoreKind);

        var result = await accounts.CreateUserAsync(new CreateCloudShellAccountUserRequest(
            "reader@example.test",
            "CloudShell123!",
            "CloudShell.Reader",
            [
                new(
                    CloudShellAuthenticationOptions.ResourceGroupClaimType,
                    CloudShellPermissions.All),
                new(
                    CloudShellAuthenticationOptions.ResourcePermissionClaimType,
                    ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
                        ContractLifecycleResourceProvider.ResourceId,
                        CloudShellPermissions.Resources.Manage))
            ]));
        var users = await accounts.ListUsersAsync();

        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        var user = Assert.Single(users, user => user.Email == "reader@example.test");
        Assert.Equal(["CloudShell.Reader"], user.Roles);
        Assert.Contains(
            user.Claims,
            claim => claim.Type == CloudShellAuthenticationOptions.ResourceGroupClaimType &&
                     claim.Value == CloudShellPermissions.All);
        Assert.Contains(
            user.Claims,
            claim => claim.Type == CloudShellAuthenticationOptions.ResourcePermissionClaimType &&
                     claim.Value == ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
                         ContractLifecycleResourceProvider.ResourceId,
                         CloudShellPermissions.Resources.Manage));
    }

    [Fact]
    public async Task BuiltInAccountService_ReportsInMemoryLocalUserStore()
    {
        await using var app = await CreateIdentityProtectedAppAsync(useInMemoryIdentity: true);
        await using var scope = app.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<CloudShellAccountService>();

        Assert.Equal(CloudShellLocalUserStoreKind.InMemory, accounts.LocalUserStoreKind);
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

    private static async Task<WebApplication> CreateProtectedAppAsync(bool includeLifecycleResource = false)
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
            ["Authentication:BuiltInAuthority:Clients:cloudshell-lifecycle:Secret"] =
                "local-development-lifecycle-secret",
            ["Authentication:BuiltInAuthority:Clients:cloudshell-lifecycle:Scopes:0"] =
                "ControlPlane.Access",
            ["Authentication:BuiltInAuthority:Clients:cloudshell-lifecycle:ResourcePermissions:0:ResourceId"] =
                ContractLifecycleResourceProvider.ResourceId,
            ["Authentication:BuiltInAuthority:Clients:cloudshell-lifecycle:ResourcePermissions:0:Permission"] =
                CloudShellPermissions.Resources.Read,
            ["Authentication:BuiltInAuthority:Clients:cloudshell-lifecycle:ResourcePermissions:1:ResourceId"] =
                ContractLifecycleResourceProvider.ResourceId,
            ["Authentication:BuiltInAuthority:Clients:cloudshell-lifecycle:ResourcePermissions:1:Permission"] =
                CloudShellPermissions.Resources.Actions.Lifecycle,
            ["Authentication:BuiltInAuthority:Clients:cloudshell-resource-manager:Secret"] =
                "local-development-resource-manager-secret",
            ["Authentication:BuiltInAuthority:Clients:cloudshell-resource-manager:Scopes:0"] =
                "ControlPlane.Access",
            ["Authentication:BuiltInAuthority:Clients:cloudshell-resource-manager:ResourcePermissions:0:ResourceId"] =
                ContractLifecycleResourceProvider.ResourceId,
            ["Authentication:BuiltInAuthority:Clients:cloudshell-resource-manager:ResourcePermissions:0:Permission"] =
                CloudShellPermissions.Resources.Manage,
            ["Persistence:Provider"] = "Sqlite",
            ["Persistence:ConnectionString"] = "Data Source=Data/cloudshell-client-auth.db",
            ["Identity:BuiltIn:Persistence:Provider"] = "Sqlite",
            ["Identity:BuiltIn:Persistence:ConnectionString"] = "Data Source=Data/identity-client-auth.db"
        });

        var controlPlane = builder.AddCloudShellControlPlane();
        if (includeLifecycleResource)
        {
            builder.Services.AddSingleton<ContractLifecycleResourceProvider>();
            builder.Services.AddSingleton<IResourceProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<ContractLifecycleResourceProvider>());
            controlPlane.Resources(resources =>
            {
                resources.Declare(
                    ContractLifecycleResourceProvider.ProviderId,
                    ContractLifecycleResourceProvider.ResourceId);
            });
        }

        var app = builder.Build();
        await app.UseCloudShellControlPlaneAsync();
        app.MapCloudShellControlPlane();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> CreateIdentityProtectedAppAsync(
        bool includeLifecycleResource = false,
        bool useInMemoryIdentity = false)
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
            ["Authentication:Mode"] = "Identity",
            ["Authentication:BuiltInAuthority:Enabled"] = "true",
            ["Authentication:BuiltInAuthority:Issuer"] = "http://localhost",
            ["Authentication:BuiltInAuthority:Audience"] = "cloudshell-control-plane",
            ["Authentication:BuiltInAuthority:Clients:cloudshell-ui:Secret"] =
                "local-development-ui-secret",
            ["Authentication:BuiltInAuthority:Clients:cloudshell-ui:Scopes:0"] =
                "ControlPlane.Access",
            ["Persistence:Provider"] = "Sqlite",
            ["Persistence:ConnectionString"] = "Data Source=Data/cloudshell-client-identity-auth.db",
            ["Identity:BuiltIn:Persistence:Provider"] = "Sqlite",
            ["Identity:BuiltIn:Persistence:ConnectionString"] = "Data Source=Data/identity-client-identity-auth.db"
        });

        var controlPlane = builder.AddCloudShellControlPlane();
        if (useInMemoryIdentity)
        {
            controlPlane.ConfigureInMemoryIdentity(_ => { });
        }

        if (includeLifecycleResource)
        {
            builder.Services.AddSingleton<ContractLifecycleResourceProvider>();
            builder.Services.AddSingleton<IResourceProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<ContractLifecycleResourceProvider>());
            controlPlane.Resources(resources =>
            {
                resources.Declare(
                    ContractLifecycleResourceProvider.ProviderId,
                    ContractLifecycleResourceProvider.ResourceId);
            });
        }

        var app = builder.Build();
        await app.UseCloudShellControlPlaneAsync();
        app.MapCloudShellControlPlane();
        await app.StartAsync();
        return app;
    }

    private static async Task CreateIdentityUserAsync(
        WebApplication app,
        string userName,
        string password,
        params Claim[] claims)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser
        {
            UserName = userName,
            Email = userName,
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(user, password);
        Assert.True(
            createResult.Succeeded,
            string.Join(", ", createResult.Errors.Select(error => error.Description)));

        foreach (var claim in claims)
        {
            var claimResult = await userManager.AddClaimAsync(user, claim);
            Assert.True(
                claimResult.Succeeded,
                string.Join(", ", claimResult.Errors.Select(error => error.Description)));
        }
    }

    private static async Task<string> GetTokenAsync(
        HttpClient client,
        string clientId = "cloudshell-test",
        string clientSecret = "local-development-client-secret")
    {
        var response = await client.PostAsync(
            "/api/auth/v1/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "ControlPlane.Access"
            }));
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return token?.AccessToken ??
            throw new InvalidOperationException("The token endpoint returned no access token.");
    }

    private static async Task<string> GetPasswordTokenAsync(
        HttpClient client,
        string userName,
        string password)
    {
        var response = await client.PostAsync(
            "/api/auth/v1/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "cloudshell-ui",
                ["client_secret"] = "local-development-ui-secret",
                ["username"] = userName,
                ["password"] = password,
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

    private sealed class ContractLifecycleResourceProvider : IResourceProvider, IResourceProcedureProvider
    {
        public const string ProviderId = "contract.lifecycle";
        public const string ResourceId = "contract:lifecycle";

        public string Id => ProviderId;

        public string DisplayName => "Contract Lifecycle";

        public List<string> ExecutedActions { get; } = [];

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                ResourceId,
                "Contract Lifecycle",
                "Lifecycle",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                TypeId: "contract.lifecycle",
                ResourceClass: ResourceClass.Executable,
                Actions:
                [
                    ResourceAction.Stop,
                    ResourceAction.Restart
                ])
        ];

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResourceProcedureResult.Completed($"Deleted {context.Resource.Id}."));

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            ExecutedActions.Add(action.Id);
            return Task.FromResult(ResourceProcedureResult.Completed($"Executed {action.Id}.") with
            {
                Signals = [ResourceProcedureSignal.Warning("Contract warning.")]
            });
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
