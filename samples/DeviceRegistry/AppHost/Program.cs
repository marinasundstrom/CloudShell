using CloudShell.Abstractions.Authorization;
using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;

var app = CloudShellDistributedApplication
    .CreateBuilder("device-registry", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "DeviceRegistry");

var registryEndpoint = app.Configuration["Samples:DeviceRegistry:RegistryEndpoint"] ??
    "http://localhost:7150";
var secretsEndpoint = app.Configuration["Samples:DeviceRegistry:SecretsEndpoint"] ??
    "http://localhost:7151";
var configurationEndpoint = app.Configuration["Samples:DeviceRegistry:ConfigurationEndpoint"] ??
    "http://localhost:7152";

app.DefineResources(resources =>
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
                    ConfigurationStoreResourceOperationPermissions.ReadSettings);
        });
});

return await app.LaunchAsync();
