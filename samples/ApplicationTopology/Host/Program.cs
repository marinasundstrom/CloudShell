using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ApplicationTopologyHost;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
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
var repositoryRootPath = Path.GetFullPath("../../..", builder.Environment.ContentRootPath);
var sampleRootPath = Path.Combine(repositoryRootPath, "samples", "ApplicationTopology");
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var secretsVaultServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.SecretsVaultService",
    "CloudShell.SecretsVaultService.csproj");

var cloudShellEndpoint = ResolveCloudShellEndpoint(builder.Configuration);
var identityIssuer = builder.Configuration["Authentication:BuiltInAuthority:Issuer"] ??
    "http://localhost";
var identityAudience = builder.Configuration["Authentication:BuiltInAuthority:Audience"] ??
    "cloudshell-control-plane";
var identitySigningKeyPem = builder.Configuration["Authentication:BuiltInAuthority:SigningKeyPem"] ??
    CreateDevelopmentSigningKeyPem();
var identityTokenEndpoint = $"{cloudShellEndpoint}/api/auth/v1/token";
var traceIngestEndpoint = builder.Configuration["Observability:TraceIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/traces/ingest";
var metricIngestEndpoint = builder.Configuration["Observability:MetricIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/metrics/ingest";
var frontendEndpoint = builder.Configuration["ApplicationTopology:FrontendEndpoint"]
    ?? "http://localhost:5218";
var apiEndpoint = builder.Configuration["ApplicationTopology:ApiEndpoint"]
    ?? "http://localhost:21422";
var apiResourceEndpoint = builder.Configuration["ApplicationTopology:GraphApiEndpoint"]
    ?? apiEndpoint;
var frontendResourceEndpoint = builder.Configuration["ApplicationTopology:GraphFrontendEndpoint"]
    ?? frontendEndpoint;
var configurationServiceBasePort =
    builder.Configuration.GetValue<int?>("ApplicationTopology:ConfigurationServiceBasePort") ??
    builder.Configuration.GetValue<int?>("ApplicationTopology:GraphConfigurationServiceBasePort") ??
    5139;
var secretsServiceBasePort =
    builder.Configuration.GetValue<int?>("ApplicationTopology:SecretsServiceBasePort") ??
    builder.Configuration.GetValue<int?>("ApplicationTopology:GraphSecretsServiceBasePort") ??
    6139;
var graphConfigurationEndpoint = builder.Configuration["ApplicationTopology:ConfigurationServiceEndpoint"]
    ?? builder.Configuration["ApplicationTopology:GraphConfigurationServiceEndpoint"]
    ?? $"http://localhost:{configurationServiceBasePort}";
var graphSecretsEndpoint = builder.Configuration["ApplicationTopology:SecretsServiceEndpoint"]
    ?? builder.Configuration["ApplicationTopology:GraphSecretsServiceEndpoint"]
    ?? $"http://localhost:{secretsServiceBasePort}";
var apiResourceEndpointUri = new Uri(apiResourceEndpoint);
var frontendResourceEndpointUri = new Uri(frontendResourceEndpoint);
var apiProjectPath = Path.Combine(
    sampleRootPath,
    "Api",
    "CloudShell.ApplicationTopologyApi.csproj");
var frontendProjectPath = Path.Combine(
    sampleRootPath,
    "Frontend",
    "CloudShell.ApplicationTopologyFrontend.csproj");
var sqlPassword = builder.Configuration["ApplicationTopology:SqlServer:Password"]
    ?? SqlServerResourceDefaults.AdministratorPassword;
var sqlPort = builder.Configuration.GetValue("ApplicationTopology:SqlServer:Port", 14334);
const string sqlServerResourceId = "application.sql-server:application-topology-sql-server";
const string sqlServerContainerName = "cloudshell-application-topology-sql-server";
const string identityProviderId = "identity:development";
const string apiIdentityName = "application-topology-api";
const string resourceGroupId = "group:application-topology";
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:BuiltInAuthority:Enabled"] = "true",
    ["Authentication:BuiltInAuthority:Issuer"] = identityIssuer,
    ["Authentication:BuiltInAuthority:Audience"] = identityAudience,
    ["Authentication:BuiltInAuthority:SigningKeyPem"] = identitySigningKeyPem,
    [SqlServerResourceDefaults.AdministratorPasswordConfigurationKey] = sqlPassword
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
                    "local-development-application-topology-api-secret"
            },
            useAsDefault: true);
        controlPlane.DefineResources(resources =>
        {
            var resourceGroup = resources.AddResourceGroup(
                resourceGroupId,
                "Application Topology",
                "Resources for the Application Topology sample.");

            var sqlVolumeResource = resources
                .AddVolume(
                    "application-topology-sql-data",
                    path: "./Data/storage/sql-server")
                .WithDisplayName("SQL Data")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false);
            var containerHostResource = resources
                .GetContainerHost()
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false);
            var sqlServerResource = resources
                .AddSqlServer("application-topology-sql-server")
                .WithDisplayName("SQL Server")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .UseContainerHost(containerHostResource)
                .WithTcpEndpoint(
                    host: "localhost",
                    port: sqlPort)
                .MountVolume(sqlVolumeResource, "/var/opt/mssql");
            var databaseResource = resources
                .AddSqlDatabase("application-topology-db")
                .WithDisplayName("SQL Database")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .BelongsToServer(sqlServerResource)
                .WithDatabaseName("application_topology")
                .EnsureCreated();
            var settingsResource = resources
                .AddConfigurationStore("application-topology-settings")
                .WithDisplayName("Settings")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .WithEndpoint(graphConfigurationEndpoint);
            settingsResourceId = settingsResource.EffectiveResourceId;
            var secretsResource = resources
                .AddSecretsVault("application-topology-secrets")
                .WithDisplayName("Secrets")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .WithEndpoint(graphSecretsEndpoint);
            secretsResourceId = secretsResource.EffectiveResourceId;
            resources
                .AddHostConfigurationSource("application-topology-host-settings")
                .WithDisplayName("Host Settings")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .WithSource("application-topology");
            var apiResource = resources
                .AddAspNetCoreProject("application-topology-api", apiProjectPath);
            var apiIdentityClientId = apiResource.IdentityClientId(apiIdentityName);
            apiResource
                .WithDisplayName("API")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .DependsOn(databaseResource)
                .DependsOn(settingsResource)
                .DependsOn(secretsResource)
                .WithHotReload(false)
                .UseLaunchSettings(false)
                .WithServiceDiscoveryName("application-topology-api")
                .WithHttpEndpoint(
                    host: apiResourceEndpointUri.Host,
                    port: apiResourceEndpointUri.Port)
                .WithIdentity(identityProviderId, name: apiIdentityName)
                .ProvisionIdentityOnStartup()
                .WithEnvironmentVariable(
                    "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
                    traceIngestEndpoint ?? string.Empty)
                .WithEnvironmentVariable(
                    "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
                    metricIngestEndpoint ?? string.Empty)
                .WithEnvironmentVariable(
                    "CLOUDSHELL_SQL_CREDENTIAL_ENDPOINT",
                    $"{cloudShellEndpoint}/api/application-topology/sql-server/v1/credentials")
                .WithEnvironmentVariable(
                    "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT",
                    identityTokenEndpoint)
                .WithEnvironmentVariable(
                    "CLOUDSHELL_IDENTITY_CLIENT_ID",
                    apiIdentityClientId)
                .WithEnvironmentVariable(
                    "CLOUDSHELL_IDENTITY_CLIENT_SECRET",
                    "local-development-application-topology-api-secret")
                .WithEnvironmentVariable(
                    "CLOUDSHELL_IDENTITY_SCOPE",
                    "ControlPlane.Access")
                .WithEnvironmentVariable(
                    "ApplicationTopology__SqlServer__Authentication",
                    "CloudShell")
                .WithEnvironmentVariable(
                    "ApplicationTopology__SqlServer__ResourceName",
                    "application-topology-sql-server")
                .WithEnvironmentVariable(
                    "ApplicationTopology__SqlServer__User",
                    "sa")
                .WithEnvironmentVariable(
                    "ApplicationTopology__SqlServer__Password",
                    sqlPassword)
                .WithEnvironmentVariable(
                    "ApplicationTopology__SqlServer__Database",
                    "application_topology")
                .WithEnvironmentVariable(
                    "OTEL_SERVICE_NAME",
                    "application-topology-api")
                .WithReference(sqlServerResource)
                .WithReference(settingsResource)
                .WithReference(secretsResource)
                .WithHttpHealthCheck(
                    "/health",
                    endpointName: "http")
                .WithHttpLivenessCheck(
                    "/alive",
                    endpointName: "http",
                    name: "alive");

            var frontendResource = resources
                .AddAspNetCoreProject("application-topology-frontend", frontendProjectPath)
                .WithDisplayName("Frontend")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .DependsOn(apiResource)
                .WithHotReload(false)
                .UseLaunchSettings(false)
                .WithHttpEndpoint(
                    host: frontendResourceEndpointUri.Host,
                    port: frontendResourceEndpointUri.Port)
                .WithEnvironmentVariable(
                    "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
                    traceIngestEndpoint ?? string.Empty)
                .WithEnvironmentVariable(
                    "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
                    metricIngestEndpoint ?? string.Empty)
                .WithEnvironmentVariable(
                    "OTEL_SERVICE_NAME",
                    "application-topology-frontend")
                .WithReference(apiResource)
                .WithHttpHealthCheck(
                    "/healthz",
                    endpointName: "http")
                .WithHttpLivenessCheck(
                    "/alive",
                    endpointName: "http",
                    name: "alive");

            sqlServerResource.Allow(apiResource, DatabaseResourceOperationPermissions.ReadWrite);
            settingsResource.Allow(apiResource, ConfigurationStoreResourceOperationPermissions.ReadEntries);
            secretsResource.Allow(apiResource, SecretsVaultResourceOperationPermissions.ReadSecrets);

            resources
                .AddDnsZone(
                    "application-topology-local",
                    zoneName: "application-topology.cloudshell.local")
                .WithDisplayName("Local DNS")
                .WithResourceGroup(resourceGroup)
                .UseLocalHostNames()
                .MapHost(
                    "app.application-topology.cloudshell.local",
                    frontendResource,
                    endpointName: "http",
                    name: "application-topology-app-local",
                    configure: mapping => mapping.WithResourceGroup(resourceGroup));
        }, AddGraphProjectionState);
    });
builder.Services
    .AddLocalSqlServerDockerRuntime(options =>
        options.AddServer(
            sqlServerResourceId,
            sqlServerContainerName,
            runtime =>
            {
                runtime.PasswordConfigurationKey = SqlServerResourceDefaults.AdministratorPasswordConfigurationKey;
                runtime.WaitUntilReady = true;
            }),
        descriptors => descriptors.AddResource(
            sqlServerResourceId,
            "application-topology.resource-model-sql-runtime.v1"));
cloudShell
    .UseSqlDatabaseResourceProvider(useResourceModelCreationHandler: true)
    .UseConfigurationStoreResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceAuthenticationIssuer = identityIssuer;
        runtime.ServiceAuthenticationAudience = identityAudience;
        runtime.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        runtime.Entries.Add(new("ApplicationTopology:Message", "Hello from CloudShell resource configuration."));
        runtime.Entries.Add(new("ApplicationTopology:Mode", "Resource model"));
    })
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = secretsVaultServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceAuthenticationIssuer = identityIssuer;
        runtime.ServiceAuthenticationAudience = identityAudience;
        runtime.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        runtime.Secrets.Add(new("ApplicationTopology--ExternalApiKey", "local-development-application-topology-api-key"));
    });

builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<ObservabilityExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapApplicationTopologyResourceModelSqlCredentialApi();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();

CloudShell.ResourceModel.ResourceState AddGraphProjectionState(
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

static string ResolveCloudShellEndpoint(IConfiguration configuration)
{
    var configuredEndpoint = FirstHttpEndpoint(configuration["Observability:Endpoint"]);
    if (configuredEndpoint is not null)
    {
        return configuredEndpoint;
    }

    var urlsEndpoint = FirstHttpEndpoint(configuration["urls"]);
    if (urlsEndpoint is not null)
    {
        return urlsEndpoint;
    }

    var aspNetCoreUrlsEndpoint = FirstHttpEndpoint(
        Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
    if (aspNetCoreUrlsEndpoint is not null)
    {
        return aspNetCoreUrlsEndpoint;
    }

    return "http://localhost:5104";
}

static string CreateDevelopmentSigningKeyPem()
{
    using var rsa = RSA.Create(2048);
    return rsa.ExportRSAPrivateKeyPem();
}

static string? FirstHttpEndpoint(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    foreach (var candidate in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            continue;
        }

        if (uri.Scheme is "http" or "https")
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }
    }

    return null;
}
