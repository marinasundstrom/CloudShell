using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Authorization;
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

var builder = CloudShellApplication.CreateBuilder(args);
var repositoryRootPath = Path.GetFullPath("../..", builder.Environment.ContentRootPath);
var deviceRegistryServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.DeviceRegistryService",
    "CloudShell.DeviceRegistryService.csproj");
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
var registryEndpoint = builder.Configuration["Samples:DeviceRegistry:RegistryEndpoint"] ??
    "http://localhost:7150";
var secretsEndpoint = builder.Configuration["Samples:DeviceRegistry:SecretsEndpoint"] ??
    "http://localhost:7151";
var configurationEndpoint = builder.Configuration["Samples:DeviceRegistry:ConfigurationEndpoint"] ??
    "http://localhost:7152";

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:AllowLocalSetup"] = "true",
    ["Authentication:BuiltInAuthority:Enabled"] = "true",
    ["Authentication:BuiltInAuthority:Issuer"] = identityIssuer,
    ["Authentication:BuiltInAuthority:Audience"] = identityAudience,
    ["Authentication:BuiltInAuthority:SigningKeyPem"] = identitySigningKeyPem
});

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            var settings = resources
                .AddConfigurationStore("device-settings")
                .WithDisplayName("Device Settings")
                .WithEndpoint(configurationEndpoint)
                .WithSeed(seed => seed.Setting(
                    "Device:Mode",
                    "factory-online"));
            var vault = resources
                .AddSecretsVault("factory")
                .WithDisplayName("Factory Trust Vault")
                .WithEndpoint(secretsEndpoint)
                .WithSeed(seed => seed.Certificate(
                    "factory-ca",
                    "local-development-factory-ca"));

            resources
                .AddDeviceRegistry("devices")
                .WithDisplayName("Factory Device Registry")
                .WithEndpoint(registryEndpoint)
                .TrustCertificate(vault.Certificate("factory-ca"))
                .UseEnrollmentProfile(profile =>
                {
                    profile
                        .AllowSubjectPrefix("device/")
                        .RequireClaim("manufacturer", "cloudshell")
                        .GrantAccess(
                            settings,
                            ConfigurationStoreResourceOperationPermissions.ReadEntries);
                });
        });
    });

cloudShell
    .UseConfigurationStoreResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceAuthenticationIssuer = identityIssuer;
        runtime.ServiceAuthenticationAudience = identityAudience;
        runtime.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        runtime.Entries.Add(new("Device:Mode", "factory-online"));
    })
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = secretsVaultServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceAuthenticationIssuer = identityIssuer;
        runtime.ServiceAuthenticationAudience = identityAudience;
        runtime.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
        runtime.Certificates.Add(new(
            "factory-ca",
            "local-development-factory-ca"));
    })
    .UseDeviceRegistryResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = deviceRegistryServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceAuthenticationIssuer = identityIssuer;
        runtime.ServiceAuthenticationAudience = identityAudience;
        runtime.ServiceAuthenticationSigningKeyPem = identitySigningKeyPem;
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
