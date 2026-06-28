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
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
using CloudShell.Providers.Docker;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
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
var graphApiEndpoint = builder.Configuration["ApplicationTopology:GraphApiEndpoint"]
    ?? apiEndpoint;
var graphFrontendEndpoint = builder.Configuration["ApplicationTopology:GraphFrontendEndpoint"]
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
var graphApiEndpointUri = new Uri(graphApiEndpoint);
var graphFrontendEndpointUri = new Uri(graphFrontendEndpoint);
var graphApiProjectPath = Path.Combine(
    sampleRootPath,
    "Api",
    "CloudShell.ApplicationTopologyApi.csproj");
var graphFrontendProjectPath = Path.Combine(
    sampleRootPath,
    "Frontend",
    "CloudShell.ApplicationTopologyFrontend.csproj");
var sqlPassword = builder.Configuration["ApplicationTopology:SqlServer:Password"]
    ?? ApplicationProviderServiceCollectionExtensions.DefaultSqlServerAdministratorPassword;
var sqlPort = builder.Configuration.GetValue("ApplicationTopology:SqlServer:Port", 14334);
const string graphApiIdentityName = "application-topology-api";
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:BuiltInAuthority:Enabled"] = "true",
    ["Authentication:BuiltInAuthority:Issuer"] = identityIssuer,
    ["Authentication:BuiltInAuthority:Audience"] = identityAudience,
    ["Authentication:BuiltInAuthority:SigningKeyPem"] = identitySigningKeyPem
});

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
IResourceDefinitionBuilder sqlStorageResource = null!;
IResourceDefinitionBuilder sqlVolumeResource = null!;
IResourceDefinitionBuilder sqlServerResource = null!;
IResourceDefinitionBuilder databaseResource = null!;
IResourceDefinitionBuilder settingsResource = null!;
IResourceDefinitionBuilder secretsResource = null!;
IResourceDefinitionBuilder hostConfigurationResource = null!;
IResourceDefinitionBuilder apiResource = null!;
IResourceDefinitionBuilder frontendResource = null!;
cloudShell.DefineResources(resources =>
{
    sqlStorageResource = resources
        .AddStorage("application-topology-local")
        .WithProvider("local")
        .WithMedium("FileSystem")
        .WithLocation("./Data/storage");
    sqlVolumeResource = resources
        .AddCloudShellVolume("application-topology-sql-data")
        .WithDisplayName("Application Topology SQL Data")
        .UseStorage(sqlStorageResource)
        .WithProvider("local")
        .WithStorageMedium("FileSystem")
        .WithSubPath("sql-server")
        .WithAccessMode("ReadWriteOnce")
        .WithPersistent();
    sqlServerResource = resources
        .AddSqlServer("application-topology-sql-server")
        .WithDisplayName("Application Topology SQL Server")
        .WithTcpEndpoint(
            host: "localhost",
            port: sqlPort)
        .MountVolume(sqlVolumeResource, "/var/opt/mssql");
    databaseResource = resources
        .AddSqlDatabase("application-topology-db")
        .BelongsToServer(sqlServerResource)
        .WithDatabaseName("application_topology")
        .EnsureCreated();
    settingsResource = resources
        .AddConfigurationStore("application-topology-settings")
        .WithDisplayName("Application Topology Settings")
        .WithEndpoint(graphConfigurationEndpoint);
    secretsResource = resources
        .AddSecretsVault("application-topology-secrets")
        .WithDisplayName("Application Topology Secrets")
        .WithEndpoint(graphSecretsEndpoint);
    hostConfigurationResource = resources
        .AddHostConfigurationSource("application-topology-host-settings")
        .WithDisplayName("Application Topology Host Settings")
        .WithSource("application-topology");
    var api = resources
        .AddAspNetCoreProject("application-topology-api", graphApiProjectPath);
    apiResource = api;
    var graphApiIdentityClientId = apiResource.IdentityClientId(graphApiIdentityName);
    api
        .WithDisplayName("Application Topology API")
        .DependsOn(databaseResource)
        .DependsOn(settingsResource)
        .DependsOn(secretsResource)
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithServiceDiscoveryName("application-topology-api")
        .WithHttpEndpoint(
            host: graphApiEndpointUri.Host,
            port: graphApiEndpointUri.Port)
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
            graphApiIdentityClientId)
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

    frontendResource = resources
        .AddAspNetCoreProject("application-topology-frontend", graphFrontendProjectPath)
        .WithDisplayName("Application Topology Frontend")
        .DependsOn(apiResource)
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithHttpEndpoint(
            host: graphFrontendEndpointUri.Host,
            port: graphFrontendEndpointUri.Port)
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
}, AddGraphProjectionState);
builder.Services
    .AddSingleton<ISqlDatabaseCreationHandler, GraphSqlDatabaseCreationHandler>()
    .AddSingleton<IApplicationTopologyDockerCommandRunner, ProcessApplicationTopologyDockerCommandRunner>()
    .AddSingleton<IApplicationTopologyGraphSqlServerRuntimeBridge, ApplicationTopologyGraphSqlServerDockerBridge>()
    .AddSingleton<IResourceOrchestrationDescriptorProvider, ApplicationTopologyGraphSqlServerOrchestrationDescriptorProvider>()
    .AddSingleton<ISqlServerRuntimeHandler, ApplicationTopologyGraphSqlServerRuntimeHandler>()
    .AddStorageBackedSqlServerResourceTypes()
    .AddSqlDatabaseResourceType()
    .AddConfigurationStoreResourceType(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        options.Entries.Add(new("ApplicationTopology:Message", "Hello from CloudShell resource configuration."));
        options.Entries.Add(new("ApplicationTopology:Mode", "Resource model"));
    })
    .AddSecretsVaultResourceType(options =>
    {
        options.ServiceProjectPath = secretsVaultServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        options.Secrets.Add(new("ApplicationTopology--ExternalApiKey", "local-development-application-topology-api-key"));
    })
    .AddHostConfigurationSourceResourceType()
    .AddAspNetCoreProjectResourceType();
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

cloudShell.AddApplicationResourceManagerUi();

cloudShell.UseLocalDevelopmentDefaults();

cloudShell.Resources(resources =>
{
    const string groupId = "group:application-topology";
    var identityProvider = resources.AddIdentityProvider(
        "identity:development",
        "Development identity",
        ResourceIdentityProviderKind.BuiltIn,
        new Dictionary<string, string>
        {
            [BuiltInResourceIdentityRegistry.ClientSecretSettingName] =
                "local-development-application-topology-api-secret"
        },
        useAsDefault: true);

    resources.AddResourceGroup(
        groupId,
        "Application Topology",
        "Resources for the Application Topology sample.");

    resources
        .Declare(sqlStorageResource)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    resources
        .Declare(sqlVolumeResource)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    var graphSqlServer = resources
        .Declare(sqlServerResource)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    resources
        .Declare(databaseResource)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    var graphSettings = resources
        .Declare(settingsResource)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    var graphSecrets = resources
        .Declare(secretsResource)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    var graphApi = resources
        .Declare(apiResource)
        .WithIdentity(identityProvider, name: graphApiIdentityName)
        .WithResourceGroup(groupId)
        .WithAutoStart(false)
        .ProvisionIdentityOnStartup();
    var graphFrontend = resources
        .Declare(frontendResource)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    resources
        .Declare(hostConfigurationResource)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);

    graphSqlServer.Allow(graphApi.Principal, DatabaseResourceOperationPermissions.ReadWrite);
    graphSettings.Allow(graphApi.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
    graphSecrets.Allow(graphApi.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);

    resources
        .AddDnsZone(
            "application-topology-local",
            zoneName: "application-topology.cloudshell.local")
        .WithDisplayName("Local DNS")
        .WithResourceGroup(groupId)
        .UseLocalHostNames()
        .MapHost("app.application-topology.cloudshell.local", graphFrontend, "http");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapApplicationTopologyGraphSqlCredentialApi();
app.MapCloudShell<App>();

app.Run();

CloudShell.ResourceDefinitions.ResourceState AddGraphProjectionState(
    CloudShell.ResourceDefinitions.ResourceState state)
{
    if (state.EffectiveResourceId != settingsResource.EffectiveResourceId &&
        state.EffectiveResourceId != secretsResource.EffectiveResourceId)
    {
        return state;
    }

    var attributes = state.ResourceAttributeValues.ToDictionary();
    if (state.EffectiveResourceId == settingsResource.EffectiveResourceId)
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
