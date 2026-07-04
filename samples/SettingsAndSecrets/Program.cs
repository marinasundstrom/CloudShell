using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;
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
const string identityProviderId = "identity:development";
const string apiIdentityName = "settings-secrets-api";
const string resourceIdentityClientSecret = "local-development-settings-secrets-api-secret";
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

string settingsResourceId = string.Empty;
string secretsResourceId = string.Empty;
var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.AddIdentityProvider(
            identityProviderId,
            "Development identity",
            ResourceIdentityProviderKind.BuiltIn,
            new Dictionary<string, string>
            {
                [BuiltInResourceIdentityRegistry.ClientSecretSettingName] =
                    resourceIdentityClientSecret
            },
            useAsDefault: true);
        controlPlane.DefineResources(resources =>
        {
            var settingsResource = resources
                .AddConfigurationStore("sample-app")
                .WithDisplayName("Sample App Settings")
                .WithEndpoint(configurationServiceEndpoint);
            settingsResourceId = settingsResource.EffectiveResourceId;
            var secretsResource = resources
                .AddSecretsVault("sample-app")
                .WithDisplayName("Sample App Secrets")
                .WithEndpoint(secretsServiceEndpoint);
            secretsResourceId = secretsResource.EffectiveResourceId;
            var api = resources
                .AddAspNetCoreProject("settings-secrets-api", apiProjectPath);
            var apiIdentityClientId = api.IdentityClientId(apiIdentityName);

            api
                .WithDisplayName("Settings and Secrets API")
                .DependsOn(settingsResource)
                .DependsOn(secretsResource)
                .WithHotReload(false)
                .UseLaunchSettings(false)
                .WithHttpEndpoint(
                    host: apiEndpointUri.Host,
                    port: apiEndpointUri.Port)
                .WithIdentity(identityProviderId, name: apiIdentityName)
                .ProvisionIdentityOnStartup()
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
                .WithEnvironmentVariable(
                    "SAMPLE_MESSAGE",
                    settingsResource.Setting("Sample:Message"))
                .WithEnvironmentVariable(
                    "SAMPLE_MODE",
                    settingsResource.Setting("Sample:Mode"))
                .WithEnvironmentVariable(
                    "SAMPLE_API_KEY",
                    secretsResource.Secret("sample-api-key"))
                .WithReference(settingsResource)
                .WithReference(secretsResource)
                .WithHttpHealthCheck(
                    "/health",
                    endpointName: "http");
            settingsResource.Allow(api, ConfigurationStoreResourceOperationPermissions.ReadEntries);
            secretsResource.Allow(api, SecretsVaultResourceOperationPermissions.ReadSecrets);
        }, AddProjectionState);
    });
cloudShell
    .UseConfigurationStoreResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceAuthenticationIssuer = identityIssuer;
        runtime.ServiceAuthenticationAudience = identityAudience;
        runtime.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        runtime.Entries.Add(new("Sample:Message", "Hello from a configuration setting"));
        runtime.Entries.Add(new("Sample:Mode", "Development"));
    })
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = secretsVaultServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceAuthenticationIssuer = identityIssuer;
        runtime.ServiceAuthenticationAudience = identityAudience;
        runtime.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        runtime.Secrets.Add(new("sample-api-key", "local-development-api-key"));
    });

builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<TelemetryExtension>()
        .AddExtension<UsageExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();

CloudShell.ResourceModel.ResourceState AddProjectionState(
    CloudShell.ResourceModel.ResourceState state)
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
