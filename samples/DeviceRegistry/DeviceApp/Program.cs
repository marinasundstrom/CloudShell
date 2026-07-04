using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;
using CloudShell.DeviceRegistry.Client;

var builder = WebApplication.CreateBuilder(args);
var registryEndpoint = builder.Configuration["DeviceRegistry:Endpoint"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT");
var registryResourceId = builder.Configuration["DeviceRegistry:ResourceId"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_DEVICE_REGISTRY_RESOURCE_ID") ??
    "iot.device-registry:devices";
var configurationEndpoint = builder.Configuration["ConfigurationStore:Endpoint"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_STORE_ENDPOINT");
var configurationResourceId = builder.Configuration["ConfigurationStore:ResourceId"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_STORE_RESOURCE_ID") ??
    "configuration.store:device-settings";
var configurationEntryName = builder.Configuration["ConfigurationStore:EntryName"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_ENTRY_NAME") ??
    "Device:Mode";
var manufacturer = builder.Configuration["Device:Manufacturer"] ??
    Environment.GetEnvironmentVariable("DEVICE_MANUFACTURER") ??
    "cloudshell";

if (string.IsNullOrWhiteSpace(registryEndpoint))
{
    throw new InvalidOperationException(
        "Set CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT to the Device Registry service endpoint.");
}

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    application = "CloudShell Device Registry sample app",
    registryResourceId
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy"
}));

app.MapPost("/enroll-current-device", async (CancellationToken cancellationToken) =>
{
    using var httpClient = new HttpClient();
    var client = new DeviceRegistryClient(new Uri(registryEndpoint), httpClient);
    var enrollment = await client.EnrollCurrentDeviceAsync(
        registryResourceId,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["manufacturer"] = manufacturer
        },
        cancellationToken: cancellationToken);

    CloudShellConfigurationEntry? configuration = null;
    if (!string.IsNullOrWhiteSpace(configurationEndpoint))
    {
        using var configurationHttpClient = new HttpClient();
        using var tokenHttpClient = new HttpClient();
        var credential = new EnvironmentCloudShellResourceCredential(
            new EnvironmentCloudShellResourceCredentialOptions
            {
                TokenEndpoint = enrollment.TokenEndpoint,
                ClientId = enrollment.ClientId,
                ClientSecret = enrollment.ClientSecret,
                Scope = ConfigurationStoreClient.DefaultScope
            },
            tokenHttpClient);
        var configurationClient = new ConfigurationStoreClient(
            BuildConfigurationEntriesEndpoint(configurationEndpoint, configurationResourceId),
            credential,
            configurationHttpClient,
            [ConfigurationStoreClient.DefaultScope]);
        configuration = await configurationClient.GetEntryAsync(
            configurationEntryName,
            cancellationToken);
    }

    return Results.Ok(new
    {
        enrollment,
        configuration
    });
});

app.Run();

static Uri BuildConfigurationEntriesEndpoint(
    string configurationEndpoint,
    string configurationResourceId) =>
    new($"{configurationEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(configurationResourceId)}/entries");
