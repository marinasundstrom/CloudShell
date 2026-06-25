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
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using System.Security.Cryptography;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

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
var apiEndpoint = builder.Configuration["Samples:SettingsAndSecrets:ApiEndpoint"] ??
    "http://localhost:5227";
var configurationServiceBasePort = builder.Configuration.GetValue<int?>(
    "Samples:SettingsAndSecrets:ConfigurationServiceBasePort") ?? 5138;
var secretsServiceBasePort = builder.Configuration.GetValue<int?>(
    "Samples:SettingsAndSecrets:SecretsServiceBasePort") ?? 6138;
var graphConfigurationServiceEndpoint = builder.Configuration["Samples:SettingsAndSecrets:GraphConfigurationServiceEndpoint"] ??
    $"http://localhost:{configurationServiceBasePort}";
var graphSecretsServiceEndpoint = builder.Configuration["Samples:SettingsAndSecrets:GraphSecretsServiceEndpoint"] ??
    $"http://localhost:{secretsServiceBasePort}";
const string graphSettingsResourceId = "configuration.store:graph-sample-app";
const string graphSecretsResourceId = "secrets.vault:graph-sample-app";
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:BuiltInAuthority:Enabled"] = "true",
    ["Authentication:BuiltInAuthority:Issuer"] = identityIssuer,
    ["Authentication:BuiltInAuthority:Audience"] = identityAudience,
    ["Authentication:BuiltInAuthority:SigningKeyPem"] = identitySigningKeyPem
});

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
builder.Services
    .AddSingleton(new ConfigurationStoreRuntimeOptions
    {
        ServiceProjectPath = configurationStoreServiceProjectPath,
        ServiceWorkingDirectory = repositoryRootPath
    })
    .AddSingleton(new SecretsVaultRuntimeOptions
    {
        ServiceProjectPath = secretsVaultServiceProjectPath,
        ServiceWorkingDirectory = repositoryRootPath
    })
    .AddInMemoryResourceModelGraph(
    [
        new ResourceGraphState(
            "graph-sample-app",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ResourceId: graphSettingsResourceId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId,
            DisplayName: "Graph Sample App Settings",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] =
                    graphConfigurationServiceEndpoint,
                [ConfigurationStoreResourceTypeProvider.Attributes.EntryCount] =
                    2
            }),
        new ResourceGraphState(
            "graph-sample-app",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ResourceId: graphSecretsResourceId,
            ProviderId: SecretsVaultResourceTypeProvider.ProviderId,
            DisplayName: "Graph Sample App Secrets",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] =
                    graphSecretsServiceEndpoint,
                [SecretsVaultResourceTypeProvider.Attributes.SecretCount] =
                    1
            })
    ])
    .AddConfigurationStoreResourceType()
    .AddSecretsVaultResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

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
        options.ServiceBasePort = configurationServiceBasePort;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
    })
    .AddSecretsProvider(options =>
    {
        options.SecretsServiceProjectPath = secretsVaultServiceProjectPath;
        options.SecretsServiceWorkingDirectory = repositoryRootPath;
        options.SecretsServiceBasePort = secretsServiceBasePort;
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
        .AddConfigurationStore("sample-app")
        .WithDisplayName("Sample App Settings")
        .WithIdentity(identityProvider)
        .WithEntries(
        [
            new("Sample:Message", "Hello from a configuration entry"),
            new("Sample:Mode", "Development")
        ]);

    var secrets = resources
        .AddSecretsVault("sample-app")
        .WithDisplayName("Sample App Secrets")
        .WithIdentity(identityProvider)
        .WithSecret("sample-api-key", "local-development-api-key");

    var api = resources
        .AddAspNetCoreProject(
            "settings-secrets-api",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
            endpoint: apiEndpoint)
        .WithIdentity(identityProvider, name: "settings-secrets-api")
        .WithReference(settings)
        .WithReference(secrets)
        .WithServiceDiscovery()
        .WithEnvironment("SAMPLE_MESSAGE", settings.Entry("Sample:Message"))
        .WithEnvironment("SAMPLE_MODE", settings.Entry("Sample:Mode"))
        .WithEnvironment("SAMPLE_API_KEY", secrets.Secret("sample-api-key"))
        .WithAutoStart(false)
        .ProvisionIdentityOnStartup();

    secrets.Allow(api.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);
    settings.Allow(api.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);

    resources.Declare(
        ResourceModelResourceProvider.DefaultProviderId,
        graphSettingsResourceId);
    resources.Declare(
        ResourceModelResourceProvider.DefaultProviderId,
        graphSecretsResourceId);
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
