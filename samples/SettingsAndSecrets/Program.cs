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
using System.Text.Json;
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
var graphApiEndpoint = builder.Configuration["Samples:SettingsAndSecrets:GraphApiEndpoint"] ??
    "http://localhost:5228";
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
builder.Services
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
            }),
        new ResourceGraphState(
            "graph-settings-secrets-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ResourceId: graphApiResourceId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            DisplayName: "Graph Settings and Secrets API",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphSettingsResourceId,
                    typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    graphSecretsResourceId,
                    typeId: SecretsVaultResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    graphApiProjectPath,
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                    false,
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                    false,
                [AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            graphApiEndpointUri.Scheme,
                            Host: graphApiEndpointUri.Host,
                            Port: graphApiEndpointUri.Port,
                            Exposure: "Local")
                    }),
                [AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_APPLICATION",
                            "Graph Settings and Secrets API"),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT",
                            identityTokenEndpoint),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_IDENTITY_CLIENT_ID",
                            graphApiIdentityClientId),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_IDENTITY_CLIENT_SECRET",
                            resourceIdentityClientSecret),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_IDENTITY_SCOPE",
                            "ControlPlane.Access")
                    }),
                [AspNetCoreProjectResourceTypeProvider.Attributes.References] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        ResourceReference.ReferenceResourceId(
                            graphSettingsResourceId,
                            typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
                        ResourceReference.ReferenceResourceId(
                            graphSecretsResourceId,
                            typeId: SecretsVaultResourceTypeProvider.ResourceTypeId)
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [ResourceHealthCheckCapabilityIds.HealthChecks] =
                    ResourceDefinitionJson.FromValue(new ResourceHealthCheckDefinitionSet(
                    [
                        ResourceHealthCheckDefinition.Http(
                            "/health",
                            endpointName: "http")
                    ]))
            })
    ])
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
                resourceIdentityClientSecret
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
    graphSettings.Allow(api.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
    graphSecrets.Allow(api.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);
    graphSettings.Allow(graphApi.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
    graphSecrets.Allow(graphApi.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);
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
