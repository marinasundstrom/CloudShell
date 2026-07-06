using System.Security.Cryptography;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceModel;

var builder = CloudShellApplication.CreateBuilder(args);

var sampleRootPath = Path.GetFullPath("..", builder.Environment.ContentRootPath);
var repositoryRootPath = Path.GetFullPath("../..", sampleRootPath);
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var secretsVaultServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.SecretsVaultService",
    "CloudShell.SecretsVaultService.csproj");
var appPath = Path.Combine(sampleRootPath, "App");
var appEndpoint = builder.Configuration["JavaScriptContainerApp:Endpoint"]
    ?? "http://localhost:5174";
var settingsServiceEndpoint = builder.Configuration["JavaScriptContainerApp:SettingsEndpoint"]
    ?? "http://localhost:5102";
var secretsServiceEndpoint = builder.Configuration["JavaScriptContainerApp:SecretsEndpoint"]
    ?? "http://localhost:6102";
var identityIssuer = builder.Configuration["Authentication:BuiltInAuthority:Issuer"] ??
    ResolveFirstUrl(builder.Configuration["urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? "http://127.0.0.1:5098");
var identityAudience = builder.Configuration["Authentication:BuiltInAuthority:Audience"] ??
    "cloudshell-control-plane";
var identitySigningKeyPem = builder.Configuration["Authentication:BuiltInAuthority:SigningKeyPem"] ??
    CreateDevelopmentSigningKeyPem();
var appEndpointUri = new Uri(appEndpoint);

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:BuiltInAuthority:Enabled"] = "true",
    ["Authentication:BuiltInAuthority:Issuer"] = identityIssuer,
    ["Authentication:BuiltInAuthority:Audience"] = identityAudience,
    ["Authentication:BuiltInAuthority:SigningKeyPem"] = identitySigningKeyPem,
    ["CloudShell:ControlPlane:BaseAddress"] = identityIssuer
});

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.ConfigureInMemoryIdentity(identity =>
        {
            identity.UseAsDefaultProvider = true;
        });
        controlPlane.DefineResources(resources =>
        {
            var group = resources.AddResourceGroup(
                "group:javascript-container-app",
                "JavaScript Container App",
                "Resources for the JavaScript container app sample.");

            var settings = resources
                .AddConfigurationStore("javascript-container-app-settings")
                .WithDisplayName("Settings")
                .WithResourceGroup(group)
                .WithEndpoint(settingsServiceEndpoint)
                .WithSeed(seed => seed.Setting(
                    "Sample--Message",
                    "Hello from the JavaScript container app host"))
                .WithAutoStart(false);

            var secrets = resources
                .AddSecretsVault("javascript-container-app-secrets")
                .WithDisplayName("Secrets")
                .WithResourceGroup(group)
                .WithEndpoint(secretsServiceEndpoint)
                .WithSeed(seed => seed.Secret(
                    "Sample--ApiKey",
                    "javascript-container-secret",
                    "v1"))
                .WithAutoStart(false);

            var frontend = resources
                .AddJavaScriptApp("javascript-container-frontend", appPath)
                .AsContainerApp(
                    tag: "dev",
                    buildContext: repositoryRootPath,
                    dockerfile: "samples/JavaScriptContainerApp/App/Dockerfile")
                .WithDisplayName("JavaScript Container Frontend")
                .WithResourceGroup(group)
                .WithAutoStart(false)
                .WithReplicas(3)
                .WithPackageManager("npm")
                .WithScript("dev")
                .WithServiceDiscovery()
                .WithReference(settings)
                .WithReference(secrets)
                .DependsOn(settings)
                .DependsOn(secrets)
                .WithHttpEndpoint(
                    host: appEndpointUri.Host,
                    port: appEndpointUri.Port,
                    targetPort: 8080)
                .WithEnvironmentVariable(
                    "PORT",
                    "8080")
                .WithEnvironmentVariable(
                    "OTEL_SERVICE_NAME",
                    "javascript-container-frontend")
                .WithHttpHealthCheck(
                    "/healthz",
                    endpointName: "http")
                .WithHttpLivenessCheck(
                    "/alive",
                    endpointName: "http")
                .RequireIdentity(name: "javascript-container-frontend")
                .ProvisionIdentityOnStartup();

            settings.Allow(frontend, ConfigurationStoreResourceOperationPermissions.ReadSettings);
            secrets.Allow(frontend, SecretsVaultResourceOperationPermissions.ReadSecrets);
        });
    });

builder.Services.AddLocalDockerContainerApplicationRuntime();

cloudShell
    .UseConfigurationStoreResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceAuthenticationIssuer = identityIssuer;
        runtime.ServiceAuthenticationAudience = identityAudience;
        runtime.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        runtime.Settings.Add(new("Sample--Message", "Hello from the JavaScript container app host"));
    })
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = secretsVaultServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceAuthenticationIssuer = identityIssuer;
        runtime.ServiceAuthenticationAudience = identityAudience;
        runtime.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        runtime.Secrets.Add(new("Sample--ApiKey", "javascript-container-secret"));
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
    "http://127.0.0.1:5098";
