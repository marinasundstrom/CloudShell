using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
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
using System.Text.Json;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

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
const string graphSqlVolumeResourceId = "storage.volume:graph-application-topology-sql-data";
const string graphSqlServerResourceId = "application.sql-server:graph-application-topology-sql-server";
const string graphDatabaseResourceId = "application.sql-database:graph-application-topology-db";
const string graphSettingsResourceId = "configuration.store:graph-application-topology-settings";
const string graphSecretsResourceId = "secrets.vault:graph-application-topology-secrets";
const string graphApiResourceId = "application.aspnet-core-project:graph-application-topology-api";
const string graphFrontendResourceId = "application.aspnet-core-project:graph-application-topology-frontend";
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
            "graph-application-topology-sql-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId,
            ResourceId: graphSqlVolumeResourceId,
            ProviderId: LocalVolumeResourceTypeProvider.ProviderId),
        new ResourceGraphState(
            "graph-application-topology-sql-server",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ResourceId: graphSqlServerResourceId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            DisplayName: "Graph Application Topology SQL Server",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SqlServerResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "tds",
                            "tcp",
                            TargetPort: 1433,
                            Host: "localhost",
                            Port: sqlPort,
                            Exposure: "Local")
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(graphSqlVolumeResourceId, "/var/opt/mssql")
                    ]))
            }),
        new ResourceGraphState(
            "graph-application-topology-db",
            SqlDatabaseResourceTypeProvider.ResourceTypeId,
            ResourceId: graphDatabaseResourceId,
            ProviderId: SqlDatabaseResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphSqlServerResourceId,
                    typeId: SqlServerResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SqlDatabaseResourceTypeProvider.Attributes.DatabaseName] = "application_topology",
                [SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated] = true
            }),
        new ResourceGraphState(
            "graph-application-topology-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ResourceId: graphSettingsResourceId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId,
            DisplayName: "Graph Application Topology Settings",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] =
                    $"http://localhost:{builder.Configuration.GetValue<int?>("ApplicationTopology:ConfigurationServiceBasePort") ?? 5138}",
                [ConfigurationStoreResourceTypeProvider.Attributes.EntryCount] =
                    2
            }),
        new ResourceGraphState(
            "graph-application-topology-secrets",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ResourceId: graphSecretsResourceId,
            ProviderId: SecretsVaultResourceTypeProvider.ProviderId,
            DisplayName: "Graph Application Topology Secrets",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] =
                    $"http://localhost:{builder.Configuration.GetValue<int?>("ApplicationTopology:SecretsServiceBasePort") ?? 6138}",
                [SecretsVaultResourceTypeProvider.Attributes.SecretCount] =
                    1
            }),
        new ResourceGraphState(
            "graph-application-topology-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ResourceId: graphApiResourceId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            DisplayName: "Graph Application Topology API",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphDatabaseResourceId,
                    typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId),
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
                [AspNetCoreProjectResourceTypeProvider.Attributes.ServiceDiscoveryName] =
                    "application-topology-api",
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
                            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
                            traceIngestEndpoint ?? string.Empty),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
                            metricIngestEndpoint ?? string.Empty),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_SQL_CREDENTIAL_ENDPOINT",
                            $"{cloudShellEndpoint}/api/sql-server/v1/credentials"),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "ApplicationTopology__SqlServer__Authentication",
                            "CloudShell"),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "ApplicationTopology__SqlServer__User",
                            "sa"),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "ApplicationTopology__SqlServer__Password",
                            sqlPassword),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "ApplicationTopology__SqlServer__Database",
                            "application_topology"),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "ApplicationTopology__Message",
                            "Hello from CloudShell graph configuration."),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "ApplicationTopology__Mode",
                            "Graph"),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "ApplicationTopology__ExternalApiKey",
                            "configured"),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "OTEL_SERVICE_NAME",
                            "graph-application-topology-api")
                    }),
                [AspNetCoreProjectResourceTypeProvider.Attributes.References] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        ResourceReference.ReferenceResourceId(
                            graphSqlServerResourceId,
                            typeId: SqlServerResourceTypeProvider.ResourceTypeId),
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
                            endpointName: "http"),
                        ResourceHealthCheckDefinition.HttpLiveness(
                            "/alive",
                            endpointName: "http",
                            name: "alive")
                    ]))
            }),
        new ResourceGraphState(
            "graph-application-topology-frontend",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ResourceId: graphFrontendResourceId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            DisplayName: "Graph Application Topology Frontend",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphApiResourceId,
                    typeId: AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    graphFrontendProjectPath,
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                    false,
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                    false,
                [AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            graphFrontendEndpointUri.Scheme,
                            Host: graphFrontendEndpointUri.Host,
                            Port: graphFrontendEndpointUri.Port,
                            Exposure: "Local")
                    }),
                [AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
                            traceIngestEndpoint ?? string.Empty),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
                            metricIngestEndpoint ?? string.Empty),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "OTEL_SERVICE_NAME",
                            "graph-application-topology-frontend")
                    }),
                [AspNetCoreProjectResourceTypeProvider.Attributes.References] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        ResourceReference.ReferenceResourceId(
                            graphApiResourceId,
                            typeId: AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [ResourceHealthCheckCapabilityIds.HealthChecks] =
                    ResourceDefinitionJson.FromValue(new ResourceHealthCheckDefinitionSet(
                    [
                        ResourceHealthCheckDefinition.Http(
                            "/healthz",
                            endpointName: "http"),
                        ResourceHealthCheckDefinition.HttpLiveness(
                            "/alive",
                            endpointName: "http",
                            name: "alive")
                    ]))
            })
    ])
    .AddLocalVolumeResourceType()
    .AddSqlServerResourceType()
    .AddSqlDatabaseResourceType()
    .AddConfigurationStoreResourceType()
    .AddSecretsVaultResourceType()
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
    })
    .UseLocalDevelopmentDefaults();

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

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSqlVolumeResourceId)
        .WithResourceGroup(groupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSqlServerResourceId)
        .WithResourceGroup(groupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphDatabaseResourceId)
        .WithResourceGroup(groupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSettingsResourceId)
        .WithResourceGroup(groupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSecretsResourceId)
        .WithResourceGroup(groupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphApiResourceId)
        .WithResourceGroup(groupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphFrontendResourceId)
        .WithResourceGroup(groupId);

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
        .WithReference(api)
        .DependsOn(api)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);

    resources
        .AddDnsZone(
            "application-topology-local",
            zoneName: "application-topology.cloudshell.local")
        .WithDisplayName("Local DNS")
        .WithResourceGroup(groupId)
        .UseLocalHostNames()
        .MapHost("app.application-topology.cloudshell.local", frontend, "http");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellSqlServerCredentialApi();
app.MapCloudShell<App>();

app.Run();

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
