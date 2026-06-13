using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);
var repositoryRootPath = Path.GetFullPath("../..", builder.Environment.ContentRootPath);
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var secretsVaultServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.SecretsVaultService",
    "CloudShell.SecretsVaultService.csproj");
var identityIssuer = builder.Configuration["Authentication:BuiltInAuthority:Issuer"] ??
    "http://localhost";
var identityAudience = builder.Configuration["Authentication:BuiltInAuthority:Audience"] ??
    "cloudshell-control-plane";
var identitySigningKeyPem = builder.Configuration["Authentication:BuiltInAuthority:SigningKeyPem"] ??
    CreateDevelopmentSigningKeyPem();
var identityTokenEndpoint = $"{ResolveFirstUrl(builder.Configuration["urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5047")}/api/auth/v1/token";
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:BuiltInAuthority:Enabled"] = "true",
    ["Authentication:BuiltInAuthority:Issuer"] = identityIssuer,
    ["Authentication:BuiltInAuthority:Audience"] = identityAudience,
    ["Authentication:BuiltInAuthority:SigningKeyPem"] = identitySigningKeyPem
});

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider(options =>
    {
        options.ResourceIdentityTokenEndpoint = identityTokenEndpoint;
    })
    .AddConfigurationProvider(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
    })
    .AddSecretsProvider(options =>
    {
        options.SecretsServiceProjectPath = secretsVaultServiceProjectPath;
        options.SecretsServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
    });

cloudShell.Resources(resources =>
{
    var identityProvider = resources.AddIdentityProvider(
        "identity:development",
        "Development identity",
        ResourceIdentityProviderKind.BuiltIn,
        new Dictionary<string, string>
        {
            [BuiltInResourceIdentityRegistry.ClientSecretSettingName] =
                "local-development-settings-secrets-api-secret"
        },
        useAsDefault: true);

    var settings = resources
        .AddConfigurationStore(
            "configuration:sample-app",
            "Sample App Settings")
        .WithIdentity(identityProvider)
        .WithEntries(
        [
            new("Sample:Message", "Hello from a configuration entry"),
            new("Sample:Mode", "Development")
        ]);

    var secrets = resources
        .AddSecretsVault("secrets-vault:sample-app", "Sample App Secrets")
        .WithIdentity(identityProvider)
        .WithSecret("sample-api-key", "local-development-api-key");

    var api = resources
        .AddAspNetCoreProject(
            "application:settings-secrets-api",
            "Settings and Secrets API",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
            endpoint: "http://localhost:5227")
        .WithIdentity(identityProvider, name: "settings-secrets-api")
        .WithEnvironment("SAMPLE_MESSAGE", settings.Entry("Sample:Message"))
        .WithEnvironment("SAMPLE_MODE", settings.Entry("Sample:Mode"))
        .WithEnvironment("SAMPLE_API_KEY", secrets.Secret("sample-api-key"))
        .WithAutoStart(false)
        .ProvisionIdentityOnStartup();

    secrets.Allow(api.Identity, SecretsVaultResourceOperationPermissions.ReadSecrets);
    settings.Allow(api.Identity, ConfigurationStoreResourceOperationPermissions.ReadEntries);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();

static string CreateDevelopmentSigningKeyPem()
{
    using var rsa = RSA.Create(2048);
    return rsa.ExportRSAPrivateKeyPem();
}

static string ResolveFirstUrl(string urls) =>
    urls
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault()
        ?.TrimEnd('/') ??
    "http://localhost:5047";
