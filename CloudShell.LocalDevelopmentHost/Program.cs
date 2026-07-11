using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);
AddDelegatedHostSettings(builder.Configuration, args);
AddLocalDevelopmentAuthenticationDefaults(builder.Configuration);

var repositoryRootPath = Path.GetFullPath("..", builder.Environment.ContentRootPath);
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var secretsVaultServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.SecretsVaultService",
    "CloudShell.SecretsVaultService.csproj");
var deviceRegistryServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.DeviceRegistryService",
    "CloudShell.DeviceRegistryService.csproj");
var eventBrokerServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.EventBrokerService",
    "CloudShell.EventBrokerService.csproj");
var cloudShellDataDirectory = ResolveCloudShellDataDirectory(
    builder.Configuration,
    builder.Environment.ContentRootPath);
var serviceAuthenticationIssuer =
    builder.Configuration["Authentication:BuiltInAuthority:Issuer"];
var serviceAuthenticationAudience =
    builder.Configuration["Authentication:BuiltInAuthority:Audience"] ??
    "cloudshell-control-plane";
var serviceAuthenticationSigningKeyPem =
    builder.Configuration["Authentication:BuiltInAuthority:SigningKeyPem"];
var hostRunApplicationResourceTypesEnabled =
    builder.Configuration.GetValue<bool>("ApplicationResources:HostRunResourceTypesEnabled");

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: options =>
    {
        options.EnableHostRunApplicationResourceTypes =
            hostRunApplicationResourceTypesEnabled;
    });
AddLocalDevelopmentIdentityProvider(builder, cloudShell);

cloudShell
    .UseConfigurationStoreResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.DefinitionsDirectory = Path.Combine(
            cloudShellDataDirectory,
            "configuration-store-definitions");
        runtime.ServiceAuthenticationIssuer = serviceAuthenticationIssuer;
        runtime.ServiceAuthenticationAudience = serviceAuthenticationAudience;
        runtime.ServiceAuthenticationSigningKeyPem = serviceAuthenticationSigningKeyPem;
    })
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = secretsVaultServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.DefinitionsDirectory = Path.Combine(
            cloudShellDataDirectory,
            "secrets-vault-definitions");
        runtime.ServiceAuthenticationIssuer = serviceAuthenticationIssuer;
        runtime.ServiceAuthenticationAudience = serviceAuthenticationAudience;
        runtime.ServiceAuthenticationSigningKeyPem = serviceAuthenticationSigningKeyPem;
    })
    .UseDeviceRegistryResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = deviceRegistryServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.DefinitionsDirectory = Path.Combine(
            cloudShellDataDirectory,
            "device-registry-definitions");
        runtime.ServiceAuthenticationIssuer = serviceAuthenticationIssuer;
        runtime.ServiceAuthenticationAudience = serviceAuthenticationAudience;
        runtime.ServiceAuthenticationSigningKeyPem = serviceAuthenticationSigningKeyPem;
        runtime.EnrollmentToken =
            builder.Configuration["CloudShell:DeviceRegistryService:EnrollmentToken"];
    })
    .UseEventBrokerResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = eventBrokerServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.DefinitionsDirectory = Path.Combine(
            cloudShellDataDirectory,
            "event-broker-definitions");
        runtime.ServiceAuthenticationIssuer = serviceAuthenticationIssuer;
        runtime.ServiceAuthenticationAudience = serviceAuthenticationAudience;
        runtime.ServiceAuthenticationSigningKeyPem = serviceAuthenticationSigningKeyPem;
    });
builder.Services.AddLocalRabbitMQDockerRuntime();
builder.Services.AddLocalDockerContainerApplicationRuntime(options =>
{
    options.AddApplication(
        "application.container-app:mower-backend",
        Path.Combine(
            repositoryRootPath,
            "samples",
            "RoboticMowerIoT",
            "Backend",
            "CloudShell.RoboticMowerIoT.Backend.csproj"),
        runtime =>
        {
            runtime.ReplicaProbePortStart =
                builder.Configuration.GetValue<int?>("RoboticMowerIoT:BackendPort") ?? 7161;
        });
});

builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<TelemetryExtension>()
        .AddExtension<UsageExtension>();
    ui.AddBuiltInProviderResourceManagerUi(options =>
    {
        options.EnableHostRunApplicationResourceTypes =
            hostRunApplicationResourceTypesEnabled;
    });
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellRabbitMQCredentialApi();
app.MapCloudShellSqlServerCredentialApi();
app.MapCloudShellUi<App>();

app.Run();

static string ResolveCloudShellDataDirectory(
    IConfiguration configuration,
    string contentRootPath)
{
    var configuredPath = configuration["CloudShell:DataDirectory"];
    var settingsBasePath = ResolveHostSettingsBasePath(configuration, contentRootPath);
    var path = string.IsNullOrWhiteSpace(configuredPath)
        ? contentRootPath
        : Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, settingsBasePath);
    Directory.CreateDirectory(path);
    return path;
}

static void AddLocalDevelopmentIdentityProvider(
    WebApplicationBuilder builder,
    IControlPlaneBuilder controlPlane)
{
    var identity = new InMemoryIdentitySetupOptions
    {
        IsConfigured = true
    };
    builder.Configuration
        .GetSection(InMemoryIdentitySetupOptions.SectionName)
        .Bind(identity);

    builder.Services.Replace(ServiceDescriptor.Singleton(identity));
    controlPlane.AddIdentityProvider(
        identity.ProviderId,
        identity.ProviderName,
        ResourceIdentityProviderKind.BuiltIn,
        useAsDefault: identity.UseAsDefaultProvider);
}

static void AddLocalDevelopmentAuthenticationDefaults(
    ConfigurationManager configuration)
{
    var defaults = new Dictionary<string, string?>();
    var configuredAuthorityEnabled =
        configuration["Authentication:BuiltInAuthority:Enabled"];
    var authorityEnabled = string.IsNullOrWhiteSpace(configuredAuthorityEnabled) ||
        bool.TryParse(configuredAuthorityEnabled, out var parsedAuthorityEnabled) &&
        parsedAuthorityEnabled;

    if (string.IsNullOrWhiteSpace(configuredAuthorityEnabled))
    {
        defaults["Authentication:BuiltInAuthority:Enabled"] = "true";
    }

    if (string.IsNullOrWhiteSpace(configuration["ResourceManager:AllowLocalPathResourceDefinitions"]))
    {
        defaults["ResourceManager:AllowLocalPathResourceDefinitions"] = "true";
    }

    if (string.IsNullOrWhiteSpace(configuration["ApplicationResources:HostRunResourceTypesEnabled"]))
    {
        defaults["ApplicationResources:HostRunResourceTypesEnabled"] = "true";
    }

    if (!authorityEnabled)
    {
        if (defaults.Count > 0)
        {
            configuration.AddInMemoryCollection(defaults);
        }

        return;
    }

    var endpoint = ResolveLocalDevelopmentControlPlaneEndpoint(configuration);
    if (string.IsNullOrWhiteSpace(configuration["Authentication:BuiltInAuthority:Issuer"]))
    {
        defaults["Authentication:BuiltInAuthority:Issuer"] = endpoint;
    }

    if (string.IsNullOrWhiteSpace(configuration["CloudShell:ControlPlane:BaseAddress"]))
    {
        defaults["CloudShell:ControlPlane:BaseAddress"] = endpoint;
    }

    if (string.IsNullOrWhiteSpace(configuration["Authentication:BuiltInAuthority:SigningKeyPem"]))
    {
        defaults["Authentication:BuiltInAuthority:SigningKeyPem"] =
            CreateDevelopmentSigningKeyPem();
    }

    if (defaults.Count > 0)
    {
        configuration.AddInMemoryCollection(defaults);
    }
}

static string ResolveLocalDevelopmentControlPlaneEndpoint(
    IConfiguration configuration)
{
    var configuredEndpoint =
        configuration["CloudShell:Launcher:ControlPlaneUrl"] ??
        configuration["CloudShell:ControlPlane:BaseAddress"] ??
        configuration["CloudShell:PublicEndpoint"];
    if (!string.IsNullOrWhiteSpace(configuredEndpoint))
    {
        return configuredEndpoint.Trim().TrimEnd('/');
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

    return "http://127.0.0.1:5112";
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

    foreach (var candidate in value.Split(
                 ';',
                 StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return candidate.Trim().TrimEnd('/');
        }
    }

    return null;
}

static string ResolveHostSettingsBasePath(
    IConfiguration configuration,
    string contentRootPath)
{
    var hostSettingsPath = configuration["CloudShell:HostSettingsPath"];
    return string.IsNullOrWhiteSpace(hostSettingsPath)
        ? contentRootPath
        : Path.GetDirectoryName(Path.GetFullPath(hostSettingsPath)) ?? contentRootPath;
}

static void AddDelegatedHostSettings(
    ConfigurationManager configuration,
    string[] args)
{
    var hostSettingsPath = configuration["CloudShell:HostSettingsPath"];
    if (string.IsNullOrWhiteSpace(hostSettingsPath))
    {
        return;
    }

    configuration
        .AddJsonFile(Path.GetFullPath(hostSettingsPath), optional: false)
        .AddEnvironmentVariables()
        .AddCommandLine(args);
}
