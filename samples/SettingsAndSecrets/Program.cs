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
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using System.Security.Cryptography;

var builder = CloudShellApplication.CreateBuilder(args);
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
var configurationServiceBasePort = builder.Configuration.GetValue<int?>(
    "Samples:SettingsAndSecrets:ConfigurationServiceBasePort") ?? 5138;
var secretsServiceBasePort = builder.Configuration.GetValue<int?>(
    "Samples:SettingsAndSecrets:SecretsServiceBasePort") ?? 6138;
var configurationServiceEndpoint = builder.Configuration["Samples:SettingsAndSecrets:ConfigurationServiceEndpoint"] ??
    $"http://localhost:{configurationServiceBasePort}";
var secretsServiceEndpoint = builder.Configuration["Samples:SettingsAndSecrets:SecretsServiceEndpoint"] ??
    $"http://localhost:{secretsServiceBasePort}";
var apiEndpoint = builder.Configuration["Samples:SettingsAndSecrets:ApiEndpoint"] ??
    "http://localhost:5228";
var apiEndpointUri = new Uri(apiEndpoint);
const string settingsResourceId = "configuration.store:sample-app";
const string secretsResourceId = "secrets.vault:sample-app";
const string apiResourceId = "application.aspnet-core-project:settings-secrets-api";
const string apiIdentityName = "settings-secrets-api";
const string resourceIdentityClientSecret = "local-development-settings-secrets-api-secret";
var apiIdentityClientId = $"{apiResourceId}/{apiIdentityName}";
var apiProjectPath = Path.Combine(
    repositoryRootPath,
    "samples",
    "CloudShell.ExampleWebApi",
    "CloudShell.ExampleWebApi.csproj");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:BuiltInAuthority:Enabled"] = "true",
    ["Authentication:BuiltInAuthority:Issuer"] = identityIssuer,
    ["Authentication:BuiltInAuthority:Audience"] = identityAudience,
    ["Authentication:BuiltInAuthority:SigningKeyPem"] = identitySigningKeyPem
});

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.DefineResources(resources =>
{
    var settings = resources
        .AddConfigurationStore("sample-app")
        .WithResourceId(settingsResourceId)
        .WithDisplayName("Sample App Settings")
        .WithEndpoint(configurationServiceEndpoint);
    var secrets = resources
        .AddSecretsVault("sample-app")
        .WithResourceId(secretsResourceId)
        .WithDisplayName("Sample App Secrets")
        .WithEndpoint(secretsServiceEndpoint);

    resources
        .AddAspNetCoreProject("settings-secrets-api", apiProjectPath)
        .WithResourceId(apiResourceId)
        .WithDisplayName("Settings and Secrets API")
        .DependsOn(settings, ConfigurationStoreResourceTypeProvider.ResourceTypeId)
        .DependsOn(secrets, SecretsVaultResourceTypeProvider.ResourceTypeId)
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .AddEndpointRequest(
            "http",
            apiEndpointUri.Scheme,
            host: apiEndpointUri.Host,
            port: apiEndpointUri.Port,
            exposure: "Local")
        .WithEnvironmentVariable(
            "CLOUDSHELL_APPLICATION",
            "Settings and Secrets API")
        .WithEnvironmentVariable(
            "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT",
            identityTokenEndpoint)
        .WithEnvironmentVariable(
            "CLOUDSHELL_IDENTITY_CLIENT_ID",
            apiIdentityClientId)
        .WithEnvironmentVariable(
            "CLOUDSHELL_IDENTITY_CLIENT_SECRET",
            resourceIdentityClientSecret)
        .WithEnvironmentVariable(
            "CLOUDSHELL_IDENTITY_SCOPE",
            "ControlPlane.Access")
        .WithReference(settings, ConfigurationStoreResourceTypeProvider.ResourceTypeId)
        .WithReference(secrets, SecretsVaultResourceTypeProvider.ResourceTypeId)
        .AddHealthCheck(ResourceHealthCheckDefinition.Http(
            "/health",
            endpointName: "http"));
}, AddProjectionState);
builder.Services
    .AddConfigurationStoreResourceType(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        options.Entries.Add(new("Sample:Message", "Hello from a configuration entry"));
        options.Entries.Add(new("Sample:Mode", "Development"));
    })
    .AddSecretsVaultResourceType(options =>
    {
        options.ServiceProjectPath = secretsVaultServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        options.Secrets.Add(new("sample-api-key", "local-development-api-key"));
    })
    .AddAspNetCoreProjectResourceType();
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

cloudShell.AddApplicationResourceManagerUi();

cloudShell.Resources(resources =>
{
    var identityProvider = resources.AddIdentityProvider(
        "identity:development",
        "Development identity",
        ResourceIdentityProviderKind.BuiltIn,
        new Dictionary<string, string>
        {
            [BuiltInResourceIdentityRegistry.ClientSecretSettingName] =
                resourceIdentityClientSecret
        },
        useAsDefault: true);

    var settings = resources.Declare(
        ResourceModelResourceProvider.DefaultProviderId,
        settingsResourceId);
    var secrets = resources.Declare(
        ResourceModelResourceProvider.DefaultProviderId,
        secretsResourceId);
    var api = resources
        .Declare(
            ResourceModelResourceProvider.DefaultProviderId,
            apiResourceId)
        .WithIdentity(identityProvider, name: apiIdentityName)
        .ProvisionIdentityOnStartup();
    settings.Allow(api.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
    secrets.Allow(api.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();

CloudShell.ResourceDefinitions.ResourceState AddProjectionState(
    CloudShell.ResourceDefinitions.ResourceState state)
{
    if (state.EffectiveResourceId != settingsResourceId &&
        state.EffectiveResourceId != secretsResourceId)
    {
        return state;
    }

    var attributes = state.ResourceAttributeValues.ToDictionary();
    if (state.EffectiveResourceId == settingsResourceId)
    {
        attributes[ConfigurationStoreResourceTypeProvider.Attributes.EntryCount] = 2;
    }
    else
    {
        attributes[SecretsVaultResourceTypeProvider.Attributes.SecretCount] = 1;
    }

    return state with { Attributes = new ResourceAttributeValueMap(attributes) };
}

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
