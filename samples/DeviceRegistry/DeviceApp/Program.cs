using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;
using CloudShell.DeviceRegistry.Client;
using System.Net.Http.Json;

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
var configurationSettingName = builder.Configuration["ConfigurationStore:SettingName"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_SETTING_NAME") ??
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

    CloudShellConfigurationSetting? configuration = null;
    DeviceMetadataResponse? heartbeat = null;
    if (!string.IsNullOrWhiteSpace(configurationEndpoint))
    {
        using var configurationHttpClient = new HttpClient();
        using var tokenHttpClient = new HttpClient();
        var accessToken = await RequestAccessTokenAsync(
            enrollment,
            tokenHttpClient,
            cancellationToken);
        heartbeat = await client.SendHeartbeatAsync(
            registryResourceId,
            enrollment.DeviceId,
            accessToken,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sample.app"] = "device-app"
            },
            "sample-app",
            cancellationToken);
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
            BuildConfigurationSettingsEndpoint(configurationEndpoint, configurationResourceId),
            credential,
            configurationHttpClient,
            [ConfigurationStoreClient.DefaultScope]);
        configuration = await configurationClient.GetSettingAsync(
            configurationSettingName,
            cancellationToken);
    }

    return Results.Ok(new
    {
        enrollment,
        heartbeat,
        configuration
    });
});

app.Run();

static Uri BuildConfigurationSettingsEndpoint(
    string configurationEndpoint,
    string configurationResourceId) =>
    new($"{configurationEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(configurationResourceId)}/entries");

static async Task<string> RequestAccessTokenAsync(
    DeviceEnrollmentResponse enrollment,
    HttpClient httpClient,
    CancellationToken cancellationToken)
{
    using var response = await httpClient.PostAsync(
        enrollment.TokenEndpoint,
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = enrollment.ClientId,
            ["client_secret"] = enrollment.ClientSecret,
            ["scope"] = ConfigurationStoreClient.DefaultScope
        }),
        cancellationToken);
    response.EnsureSuccessStatusCode();
    var token = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(
        cancellationToken: cancellationToken);
    return token is not null &&
        token.TryGetValue("access_token", out var accessToken) &&
        accessToken is not null
            ? accessToken.ToString() ?? string.Empty
            : throw new InvalidOperationException("CloudShell token response did not include an access token.");
}
