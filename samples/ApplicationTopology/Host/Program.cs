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
var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"]
    ?? cloudShellEndpoint;
var otlpProtocol = builder.Configuration["Observability:OtlpProtocol"];
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
var graphOnly = builder.Configuration.GetValue("ApplicationTopology:GraphOnly", true);
var graphConfigurationEndpoint = builder.Configuration["ApplicationTopology:GraphConfigurationServiceEndpoint"]
    ?? $"http://localhost:{builder.Configuration.GetValue<int?>("ApplicationTopology:GraphConfigurationServiceBasePort") ?? 5139}";
var graphSecretsEndpoint = builder.Configuration["ApplicationTopology:GraphSecretsServiceEndpoint"]
    ?? $"http://localhost:{builder.Configuration.GetValue<int?>("ApplicationTopology:GraphSecretsServiceBasePort") ?? 6139}";
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
const string graphSqlStorageResourceId = "cloudshell.storage:graph-application-topology-local";
const string graphSqlVolumeResourceId = "cloudshell.volume:graph-application-topology-sql-data";
const string graphSqlServerResourceId = "application.sql-server:graph-application-topology-sql-server";
const string graphDatabaseResourceId = "application.sql-database:graph-application-topology-db";
const string graphSettingsResourceId = "configuration.store:graph-application-topology-settings";
const string graphSecretsResourceId = "secrets.vault:graph-application-topology-secrets";
const string graphApiResourceId = "application.aspnet-core-project:graph-application-topology-api";
const string graphFrontendResourceId = "application.aspnet-core-project:graph-application-topology-frontend";
const string graphHostConfigurationResourceId = "configuration.host:graph-application-topology-host-settings";
const string graphApiIdentityName = "graph-application-topology-api";
var graphApiIdentityClientId = $"{graphApiResourceId}/{graphApiIdentityName}";
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
    var graphSqlStorage = resources
        .AddStorage("graph-application-topology-local")
        .WithResourceId(graphSqlStorageResourceId)
        .WithProvider("local")
        .WithMedium("FileSystem")
        .WithLocation("./Data/storage");
    var graphSqlVolume = resources
        .AddCloudShellVolume("graph-application-topology-sql-data")
        .WithResourceId(graphSqlVolumeResourceId)
        .WithDisplayName("Graph Application Topology SQL Data")
        .UseStorage(graphSqlStorage)
        .WithProvider("local")
        .WithStorageMedium("FileSystem")
        .WithSubPath("sql-server")
        .WithAccessMode("ReadWriteOnce")
        .WithPersistent();
    var graphSqlServer = resources
        .AddSqlServer("graph-application-topology-sql-server")
        .WithResourceId(graphSqlServerResourceId)
        .WithDisplayName("Graph Application Topology SQL Server")
        .AddEndpointRequest(
            "tds",
            "tcp",
            targetPort: 1433,
            host: "localhost",
            port: sqlPort,
            exposure: "Local")
        .MountVolume(graphSqlVolume, "/var/opt/mssql");
    var graphDatabase = resources
        .AddSqlDatabase("graph-application-topology-db")
        .WithResourceId(graphDatabaseResourceId)
        .BelongsToServer(graphSqlServer)
        .WithDatabaseName("application_topology")
        .EnsureCreated();
    var graphSettings = resources
        .AddConfigurationStore("graph-application-topology-settings")
        .WithResourceId(graphSettingsResourceId)
        .WithDisplayName("Graph Application Topology Settings")
        .WithEndpoint(graphConfigurationEndpoint);
    var graphSecrets = resources
        .AddSecretsVault("graph-application-topology-secrets")
        .WithResourceId(graphSecretsResourceId)
        .WithDisplayName("Graph Application Topology Secrets")
        .WithEndpoint(graphSecretsEndpoint);
    resources
        .AddHostConfigurationSource("graph-application-topology-host-settings")
        .WithResourceId(graphHostConfigurationResourceId)
        .WithDisplayName("Graph Application Topology Host Settings")
        .WithSource("application-topology");
    var graphApi = resources
        .AddAspNetCoreProject("graph-application-topology-api", graphApiProjectPath)
        .WithResourceId(graphApiResourceId)
        .WithDisplayName("Graph Application Topology API")
        .DependsOn(graphDatabase, SqlDatabaseResourceTypeProvider.ResourceTypeId)
        .DependsOn(graphSettings, ConfigurationStoreResourceTypeProvider.ResourceTypeId)
        .DependsOn(graphSecrets, SecretsVaultResourceTypeProvider.ResourceTypeId)
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithServiceDiscoveryName("application-topology-api")
        .AddEndpointRequest(
            "http",
            graphApiEndpointUri.Scheme,
            host: graphApiEndpointUri.Host,
            port: graphApiEndpointUri.Port,
            exposure: "Local")
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
            "graph-application-topology-sql-server")
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
            "graph-application-topology-api")
        .WithReference(graphSqlServer, SqlServerResourceTypeProvider.ResourceTypeId)
        .WithReference(graphSettings, ConfigurationStoreResourceTypeProvider.ResourceTypeId)
        .WithReference(graphSecrets, SecretsVaultResourceTypeProvider.ResourceTypeId)
        .AddHealthCheck(ResourceHealthCheckDefinition.Http(
            "/health",
            endpointName: "http"))
        .AddHealthCheck(ResourceHealthCheckDefinition.HttpLiveness(
            "/alive",
            endpointName: "http",
            name: "alive"));

    resources
        .AddAspNetCoreProject("graph-application-topology-frontend", graphFrontendProjectPath)
        .WithResourceId(graphFrontendResourceId)
        .WithDisplayName("Graph Application Topology Frontend")
        .DependsOn(graphApi, AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .AddEndpointRequest(
            "http",
            graphFrontendEndpointUri.Scheme,
            host: graphFrontendEndpointUri.Host,
            port: graphFrontendEndpointUri.Port,
            exposure: "Local")
        .WithEnvironmentVariable(
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            traceIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
            metricIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "OTEL_SERVICE_NAME",
            "graph-application-topology-frontend")
        .WithReference(graphApi, AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        .AddHealthCheck(ResourceHealthCheckDefinition.Http(
            "/healthz",
            endpointName: "http"))
        .AddHealthCheck(ResourceHealthCheckDefinition.HttpLiveness(
            "/alive",
            endpointName: "http",
            name: "alive"));
}, AddGraphProjectionState);
builder.Services
    .AddSingleton<ISqlDatabaseCreationHandler, GraphSqlDatabaseCreationHandler>();
if (graphOnly)
{
    builder.Services
        .AddSingleton<IApplicationTopologyDockerCommandRunner, ProcessApplicationTopologyDockerCommandRunner>()
        .AddSingleton<IApplicationTopologyGraphSqlServerRuntimeBridge, ApplicationTopologyGraphSqlServerDockerBridge>()
        .AddSingleton<IResourceOrchestrationDescriptorProvider, ApplicationTopologyGraphSqlServerOrchestrationDescriptorProvider>();
}
else
{
    builder.Services
        .AddSingleton<IApplicationTopologyGraphSqlServerRuntimeBridge, ApplicationTopologyGraphSqlServerResourceManagerBridge>();
}

builder.Services
    .AddSingleton<ISqlServerRuntimeHandler, ApplicationTopologyGraphSqlServerRuntimeHandler>()
    .AddStorageResourceType()
    .AddCloudShellVolumeResourceType()
    .AddSqlServerResourceType()
    .AddSqlDatabaseResourceType()
    .AddConfigurationStoreResourceType(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        options.Entries.Add(new("ApplicationTopology:Message", "Hello from CloudShell graph configuration."));
        options.Entries.Add(new("ApplicationTopology:Mode", "Graph"));
    })
    .AddSecretsVaultResourceType(options =>
    {
        options.ServiceProjectPath = secretsVaultServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceAuthenticationIssuer = identityIssuer;
        options.ServiceAuthenticationAudience = identityAudience;
        options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        options.Secrets.Add(new("ApplicationTopology--ExternalApiKey", "graph-local-development-api-key"));
    })
    .AddHostConfigurationSourceResourceType()
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
            options.LogStore = builder.Configuration["Observability:ApplicationLogs:Store"] ?? options.LogStore;
            options.LogDirectory = builder.Configuration["Observability:ApplicationLogs:LogDirectory"] ??
                options.LogDirectory;
            options.LogRetentionDays = builder.Configuration.GetValue<int?>(
                "Observability:ApplicationLogs:LogRetentionDays") ?? options.LogRetentionDays;
            options.RetainedLogEntries = builder.Configuration.GetValue<int?>(
                "Observability:ApplicationLogs:RetainedLogEntries") ?? options.RetainedLogEntries;
            options.SplitLogFilesByDay = builder.Configuration.GetValue<bool?>(
                "Observability:ApplicationLogs:SplitLogFilesByDay") ?? options.SplitLogFilesByDay;
            options.OtlpEndpoint = otlpEndpoint;
            options.OtlpProtocol = otlpProtocol;
            options.ResourceIdentityTokenEndpoint = identityTokenEndpoint;
        })
        .AddConfigurationProvider(options =>
        {
            options.ServiceProjectPath = configurationStoreServiceProjectPath;
            options.ServiceWorkingDirectory = repositoryRootPath;
            options.ServiceBasePort = builder.Configuration.GetValue<int?>(
                "ApplicationTopology:ConfigurationServiceBasePort") ?? options.ServiceBasePort;
            options.ServiceAuthenticationIssuer = identityIssuer;
            options.ServiceAuthenticationAudience = identityAudience;
            options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        })
        .AddSecretsProvider(options =>
        {
            options.SecretsServiceProjectPath = secretsVaultServiceProjectPath;
            options.SecretsServiceWorkingDirectory = repositoryRootPath;
            options.SecretsServiceBasePort = builder.Configuration.GetValue<int?>(
                "ApplicationTopology:SecretsServiceBasePort") ?? options.SecretsServiceBasePort;
            options.ServiceAuthenticationIssuer = identityIssuer;
            options.ServiceAuthenticationAudience = identityAudience;
            options.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        });
}
else
{
    cloudShell.AddApplicationResourceManagerUi();
}

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

    IResourceBuilder? oldApi = null;
    if (!graphOnly)
    {
        var localStorage = resources
            .AddLocalStorage("application-topology-local")
            .WithDisplayName("Local Storage")
            .WithResourceGroup(groupId)
            .UseLocation("./Data/storage");

        var sqlData = resources
            .AddVolume("application-topology-sql-data")
            .WithDisplayName("SQL Data")
            .WithResourceGroup(groupId)
            .UseStorage(localStorage, "sql-server")
            .WithAccessMode(VolumeAccessMode.ReadWriteOnce);

        var sqlServer = resources
            .AddSqlServer(
                "application-topology-sql-server",
                administratorPassword: sqlPassword,
                dataVolume: sqlData,
                port: sqlPort)
            .DeclareDatabase("application_topology", "Application Topology")
                .EnsureCreated()
            .WithIdentity(identityProvider, name: "application-topology-sql-server")
            .WithResourceGroup(groupId)
            .WithAutoStart(false)
            .ProvisionIdentityOnStartup();

        var settings = resources
            .AddConfigurationStore("application-topology")
            .WithDisplayName("Settings")
            .WithIdentity(identityProvider)
            .WithResourceGroup(groupId)
            .WithEntries(
            [
                new("ApplicationTopology:Message", "Hello from CloudShell configuration."),
                new("ApplicationTopology:Mode", "Development")
            ])
            .ProvisionIdentityOnStartup();

        var secrets = resources
            .AddSecretsVault("application-topology")
            .WithDisplayName("Secrets")
            .WithIdentity(identityProvider)
            .WithResourceGroup(groupId)
            .WithSecret("external-api-key", "local-development-api-key")
            .ProvisionIdentityOnStartup();

        var api = resources.AddAspNetCoreProject(
            "application-topology-api",
            "../Api/CloudShell.ApplicationTopologyApi.csproj",
            endpoint: apiEndpoint)
            .WithIdentity(identityProvider, name: "application-topology-api")
            .WithHttpHealthCheck("/health")
            .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
            .WithRecovery(new ResourceRecoveryPolicy(
                Enabled: true,
                ProbeType: ResourceProbeType.Liveness,
                FailureThreshold: 3,
                InitialBackoffSeconds: 5,
                MaxBackoffSeconds: 60,
                MaxAttempts: 3))
            .WithOtlpExporter(otlpEndpoint, otlpProtocol)
            .WithEnvironment("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint ?? string.Empty)
            .WithEnvironment("CLOUDSHELL_METRIC_INGEST_ENDPOINT", metricIngestEndpoint ?? string.Empty)
            .WithEnvironment("CLOUDSHELL_SQL_CREDENTIAL_ENDPOINT", $"{cloudShellEndpoint}/api/sql-server/v1/credentials")
            .WithEnvironment("ApplicationTopology__SqlServer__Authentication", "CloudShell")
            .WithEnvironment("ApplicationTopology__SqlServer__ResourceName", "application-topology-sql-server")
            .WithEnvironment("ApplicationTopology__SqlServer__User", "sa")
            .WithEnvironment("ApplicationTopology__SqlServer__Password", sqlPassword)
            .WithEnvironment("ApplicationTopology__SqlServer__Database", "application_topology")
            .WithEnvironment("ApplicationTopology__Message", settings.Entry("ApplicationTopology:Message"))
            .WithEnvironment("ApplicationTopology__Mode", settings.Entry("ApplicationTopology:Mode"))
            .WithEnvironment("ApplicationTopology__ExternalApiKey", secrets.Secret("external-api-key"))
            .WithReference(settings)
            .WithReference(secrets)
            .WithReference(sqlServer)
            .DependsOn(sqlServer)
            .WithResourceGroup(groupId)
            .WithAutoStart(false)
            .ProvisionIdentityOnStartup();

        secrets.Allow(api.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);
        settings.Allow(api.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
        sqlServer.Allow(api.Principal, DatabaseResourceOperationPermissions.ReadWrite);
        oldApi = api;
    }

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSqlStorageResourceId)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSqlVolumeResourceId)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    var graphSqlServer = resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSqlServerResourceId)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphDatabaseResourceId)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    var graphSettings = resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSettingsResourceId)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    var graphSecrets = resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSecretsResourceId)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    var graphApi = resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphApiResourceId)
        .WithIdentity(identityProvider, name: graphApiIdentityName)
        .WithResourceGroup(groupId)
        .WithAutoStart(false)
        .ProvisionIdentityOnStartup();
    var graphFrontend = resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphFrontendResourceId)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphHostConfigurationResourceId)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);

    graphSqlServer.Allow(graphApi.Principal, DatabaseResourceOperationPermissions.ReadWrite);
    graphSettings.Allow(graphApi.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
    graphSecrets.Allow(graphApi.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);

    IResourceBuilder frontendNameMappingTarget = graphFrontend;
    if (!graphOnly)
    {
        var frontendApi = oldApi ??
            throw new InvalidOperationException("The old ApplicationTopology API declaration is required when graph-only mode is disabled.");
        var frontend = resources
            .AddAspNetCoreProject(
                "application-topology-frontend",
                "../Frontend/CloudShell.ApplicationTopologyFrontend.csproj",
                endpoint: frontendEndpoint)
            .WithHttpHealthCheck("/healthz")
            .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
            .WithOtlpExporter(otlpEndpoint, otlpProtocol)
            .WithEnvironment("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint ?? string.Empty)
            .WithEnvironment("CLOUDSHELL_METRIC_INGEST_ENDPOINT", metricIngestEndpoint ?? string.Empty)
            .WithServiceDiscovery()
            .WithReference(frontendApi)
            .DependsOn(frontendApi)
            .WithResourceGroup(groupId)
            .WithAutoStart(false);
        frontendNameMappingTarget = frontend;
    }

    resources
        .AddDnsZone(
            "application-topology-local",
            zoneName: "application-topology.cloudshell.local")
        .WithDisplayName("Local DNS")
        .WithResourceGroup(groupId)
        .UseLocalHostNames()
        .MapHost("app.application-topology.cloudshell.local", frontendNameMappingTarget, "http");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
if (!graphOnly)
{
    app.MapCloudShellSqlServerCredentialApi();
}

app.MapApplicationTopologyGraphSqlCredentialApi();
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
