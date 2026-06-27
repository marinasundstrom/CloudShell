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
var graphApiEndpoint = builder.Configuration["Samples:SettingsAndSecrets:GraphApiEndpoint"] ??
    "http://localhost:5228";
var graphOnly = builder.Configuration.GetValue("Samples:SettingsAndSecrets:GraphOnly", true);
var graphApiEndpointUri = new Uri(graphApiEndpoint);
const string graphSettingsResourceId = "configuration.store:graph-sample-app";
const string graphSecretsResourceId = "secrets.vault:graph-sample-app";
const string graphApiResourceId = "application.aspnet-core-project:graph-settings-secrets-api";
const string graphApiIdentityName = "graph-settings-secrets-api";
const string resourceIdentityClientSecret = "local-development-settings-secrets-api-secret";
var graphApiIdentityClientId = $"{graphApiResourceId}/{graphApiIdentityName}";
var graphApiProjectPath = Path.Combine(
    repositoryRootPath,
    "samples",
    "CloudShell.ExampleWebApi",
    "CloudShell.ExampleWebApi.csproj");
var graphConfigurationEntriesEndpoint =
    $"{graphConfigurationServiceEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(graphSettingsResourceId)}/entries";
var graphSecretsEndpoint =
    $"{graphSecretsServiceEndpoint.TrimEnd('/')}/api/secrets/vaults/{Uri.EscapeDataString(graphSecretsResourceId)}/secrets";
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
    var graphSettings = resources
        .AddConfigurationStore("graph-sample-app")
        .WithResourceId(graphSettingsResourceId)
        .WithDisplayName("Graph Sample App Settings")
        .WithEndpoint(graphConfigurationServiceEndpoint);
    var graphSecrets = resources
        .AddSecretsVault("graph-sample-app")
        .WithResourceId(graphSecretsResourceId)
        .WithDisplayName("Graph Sample App Secrets")
        .WithEndpoint(graphSecretsServiceEndpoint);

    resources
        .AddAspNetCoreProject("graph-settings-secrets-api", graphApiProjectPath)
        .WithResourceId(graphApiResourceId)
        .WithDisplayName("Graph Settings and Secrets API")
        .DependsOn(graphSettings, ConfigurationStoreResourceTypeProvider.ResourceTypeId)
        .DependsOn(graphSecrets, SecretsVaultResourceTypeProvider.ResourceTypeId)
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .AddEndpointRequest(
            "http",
            graphApiEndpointUri.Scheme,
            host: graphApiEndpointUri.Host,
            port: graphApiEndpointUri.Port,
            exposure: "Local")
        .WithEnvironmentVariable(
            "CLOUDSHELL_APPLICATION",
            "Graph Settings and Secrets API")
        .WithEnvironmentVariable(
            "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT",
            identityTokenEndpoint)
        .WithEnvironmentVariable(
            "CLOUDSHELL_IDENTITY_CLIENT_ID",
            graphApiIdentityClientId)
        .WithEnvironmentVariable(
            "CLOUDSHELL_IDENTITY_CLIENT_SECRET",
            resourceIdentityClientSecret)
        .WithEnvironmentVariable(
            "CLOUDSHELL_IDENTITY_SCOPE",
            "ControlPlane.Access")
        .WithReference(graphSettings, ConfigurationStoreResourceTypeProvider.ResourceTypeId)
        .WithReference(graphSecrets, SecretsVaultResourceTypeProvider.ResourceTypeId)
        .AddHealthCheck(ResourceHealthCheckDefinition.Http(
            "/health",
            endpointName: "http"));
}, AddGraphProjectionState);
builder.Services
    .AddConfigurationStoreResourceType(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        options.Entries.Add(new("Sample:Message", "Hello from a graph configuration entry"));
        options.Entries.Add(new("Sample:Mode", "Graph"));
    })
    .AddSecretsVaultResourceType(options =>
    {
        options.ServiceProjectPath = secretsVaultServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        options.Secrets.Add(new("sample-api-key", "graph-local-development-api-key"));
    })
    .AddAspNetCoreProjectResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

if (!graphOnly)
{
    cloudShell
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
}

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

    var graphSettings = resources.Declare(
        ResourceModelResourceProvider.DefaultProviderId,
        graphSettingsResourceId);
    var graphSecrets = resources.Declare(
        ResourceModelResourceProvider.DefaultProviderId,
        graphSecretsResourceId);
    var graphApi = resources
        .Declare(
            ResourceModelResourceProvider.DefaultProviderId,
            graphApiResourceId)
        .WithIdentity(identityProvider, name: graphApiIdentityName)
        .ProvisionIdentityOnStartup();
    graphSettings.Allow(graphApi.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
    graphSecrets.Allow(graphApi.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);

    if (!graphOnly)
    {
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
            .WithEnvironment("CLOUDSHELL_CONFIGURATION_SERVICE_NAME", "graph-sample-app")
            .WithEnvironment("CLOUDSHELL_CONFIGURATION_GRAPH_SAMPLE_APP_STORE_ID", graphSettingsResourceId)
            .WithEnvironment("CLOUDSHELL_CONFIGURATION_GRAPH_SAMPLE_APP_ENDPOINT", graphConfigurationEntriesEndpoint)
            .WithEnvironment("CLOUDSHELL_SECRETS_VAULT_NAME", "graph-sample-app")
            .WithEnvironment("CLOUDSHELL_SECRETS_GRAPH_SAMPLE_APP_VAULT_ID", graphSecretsResourceId)
            .WithEnvironment("CLOUDSHELL_SECRETS_GRAPH_SAMPLE_APP_ENDPOINT", graphSecretsEndpoint)
            .WithAutoStart(false)
            .ProvisionIdentityOnStartup();

        secrets.Allow(api.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);
        settings.Allow(api.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
        graphSettings.Allow(api.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
        graphSecrets.Allow(api.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);
    }
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();

CloudShell.ResourceDefinitions.ResourceState AddGraphProjectionState(
    CloudShell.ResourceDefinitions.ResourceState state)
{
    if (state.EffectiveResourceId != graphSettingsResourceId &&
        state.EffectiveResourceId != graphSecretsResourceId)
    {
        return state;
    }

    var attributes = state.ResourceAttributeValues.ToDictionary();
    if (state.EffectiveResourceId == graphSettingsResourceId)
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
