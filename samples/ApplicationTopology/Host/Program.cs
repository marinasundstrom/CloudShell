using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
using CloudShell.Providers.Docker;
using CloudShell.ApplicationTopology;

var builder = CloudShellApplication.CreateBuilder(args);
var repositoryRootPath = Path.GetFullPath("../../..", builder.Environment.ContentRootPath);
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var secretsVaultServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.SecretsVaultService",
    "CloudShell.SecretsVaultService.csproj");

var cloudShellEndpoint = ResolveCloudShellEndpoint(builder.Configuration);
var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"]
    ?? cloudShellEndpoint;
var otlpProtocol = builder.Configuration["Observability:OtlpProtocol"];
var traceIngestEndpoint = builder.Configuration["Observability:TraceIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/traces/ingest";
var frontendEndpoint = builder.Configuration["ApplicationTopology:FrontendEndpoint"]
    ?? "http://localhost:5218";
var sqlPassword = builder.Configuration["ApplicationTopology:SqlServer:Password"]
    ?? SqlServerResourceBuilderExtensions.DefaultPassword;
var sqlPort = builder.Configuration.GetValue("ApplicationTopology:SqlServer:Port", 14334);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider(options =>
    {
        options.OtlpEndpoint = otlpEndpoint;
        options.OtlpProtocol = otlpProtocol;
    })
    .AddConfigurationProvider(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceBasePort = builder.Configuration.GetValue<int?>(
            "ApplicationTopology:ConfigurationServiceBasePort") ?? options.ServiceBasePort;
    })
    .AddSecretsProvider(options =>
    {
        options.SecretsServiceProjectPath = secretsVaultServiceProjectPath;
        options.SecretsServiceWorkingDirectory = repositoryRootPath;
        options.SecretsServiceBasePort = builder.Configuration.GetValue<int?>(
            "ApplicationTopology:SecretsServiceBasePort") ?? options.SecretsServiceBasePort;
    })
    .UseLocalDevelopmentDefaults();

cloudShell.Resources(resources =>
{
    const string groupId = "group:application-topology";

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
            password: sqlPassword,
            dataVolume: sqlData,
            port: sqlPort)
        .WithResourceGroup(groupId)
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithAutoStart(false);

    var settings = resources
        .AddConfigurationStore("application-topology")
        .WithDisplayName("Settings")
        .WithResourceGroup(groupId)
        .WithEntries(
        [
            new("ApplicationTopology:Message", "Hello from CloudShell configuration."),
            new("ApplicationTopology:Mode", "Development")
        ]);

    var secrets = resources
        .AddSecretsVault("application-topology")
        .WithDisplayName("Secrets")
        .WithResourceGroup(groupId)
        .WithSecret("external-api-key", "local-development-api-key");

    var api = resources.AddAspNetCoreProject(
        "application-topology-api",
        "../Api/CloudShell.ApplicationTopologyApi.csproj")
        .WithHttpHealthCheck("/health")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
        .WithOtlpExporter(otlpEndpoint, otlpProtocol)
        .WithEnvironment("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint ?? string.Empty)
        .WithEnvironment("ApplicationTopology__SqlServer__User", "sa")
        .WithEnvironment("ApplicationTopology__SqlServer__Password", sqlPassword)
        .WithEnvironment("ApplicationTopology__Message", settings.Entry("ApplicationTopology:Message"))
        .WithEnvironment("ApplicationTopology__Mode", settings.Entry("ApplicationTopology:Mode"))
        .WithEnvironment("ApplicationTopology__ExternalApiKey", secrets.Secret("external-api-key"))
        .WithReference(settings)
        .WithReference(secrets)
        .WithReference(sqlServer)
        .DependsOn(sqlServer)
        .WithResourceGroup(groupId)
        .WithAutoStart(false);

    var frontend = resources
        .AddAspNetCoreProject(
            "application-topology-frontend",
            "../Frontend/CloudShell.ApplicationTopologyFrontend.csproj",
            endpoint: frontendEndpoint)
        .WithHttpHealthCheck("/healthz")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
        .WithOtlpExporter(otlpEndpoint, otlpProtocol)
        .WithEnvironment("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint ?? string.Empty)
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
