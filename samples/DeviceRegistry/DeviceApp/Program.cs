using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;
using CloudShell.DeviceRegistry.Client;
using CloudShell.EventBroker.Client;
using System.Net.Http.Json;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var registryEndpoint = builder.Configuration["DeviceRegistry:Endpoint"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT");
var registryResourceId = builder.Configuration["DeviceRegistry:ResourceId"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_DEVICE_REGISTRY_RESOURCE_ID") ??
    "iot.device-registry:devices";
var registryMqttEndpoint = builder.Configuration["DeviceRegistry:MqttEndpoint"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_DEVICE_REGISTRY_MQTT_ENDPOINT");
var configurationEndpoint = builder.Configuration["ConfigurationStore:Endpoint"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_STORE_ENDPOINT");
var configurationResourceId = builder.Configuration["ConfigurationStore:ResourceId"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_STORE_RESOURCE_ID") ??
    "configuration.store:device-settings";
var configurationSettingName = builder.Configuration["ConfigurationStore:SettingName"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_SETTING_NAME") ??
    "Device:Mode";
var eventBrokerEndpoint = builder.Configuration["EventBroker:Endpoint"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_EVENT_BROKER_ENDPOINT");
var eventBrokerResourceId = builder.Configuration["EventBroker:ResourceId"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_EVENT_BROKER_RESOURCE_ID") ??
    "event.broker:events";
var eventBrokerStream = builder.Configuration["EventBroker:Stream"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_EVENT_BROKER_STREAM") ??
    "device-checkins";
var manufacturer = builder.Configuration["Device:Manufacturer"] ??
    Environment.GetEnvironmentVariable("DEVICE_MANUFACTURER") ??
    "cloudshell";
var enrollmentToken = builder.Configuration["DeviceRegistry:EnrollmentToken"] ??
    Environment.GetEnvironmentVariable("CLOUDSHELL_DEVICE_REGISTRY_ENROLLMENT_TOKEN");

if (string.IsNullOrWhiteSpace(registryEndpoint))
{
    throw new InvalidOperationException(
        "Set CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT to the Device Registry service endpoint.");
}

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    application = "CloudShell Device Registry sample app",
    registryResourceId,
    registryMqttEndpoint
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy"
}));

app.MapPost("/enroll-current-device", async (CancellationToken cancellationToken) =>
{
    using var httpClient = new HttpClient();
    var client = new DeviceRegistryClient(new Uri(registryEndpoint), httpClient);
    app.Logger.LogInformation(
        "Enrolling current device with CloudShell Device Registry {RegistryResourceId}.",
        registryResourceId);
    var enrollment = await client.EnrollCurrentDeviceAsync(
        registryResourceId,
        enrollmentToken ?? throw new InvalidOperationException(
            "Set CLOUDSHELL_DEVICE_REGISTRY_ENROLLMENT_TOKEN to the Device Registry enrollment token."),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["manufacturer"] = manufacturer
        },
        cancellationToken: cancellationToken);
    app.Logger.LogInformation(
        "CloudShell Device Registry enrollment completed for device {DeviceId} with subject {Subject}.",
        enrollment.DeviceId,
        enrollment.Subject);

    CloudShellConfigurationSetting? configuration = null;
    DeviceMetadataResponse? heartbeat = null;
    DeviceSyncResponse? sync = null;
    DeviceSyncResponse? mqttSync = null;
    EventBrokerEvent? publishedEvent = null;
    var mqttSyncPublished = false;
    if (!string.IsNullOrWhiteSpace(configurationEndpoint))
    {
        using var configurationHttpClient = new HttpClient();
        using var tokenHttpClient = new HttpClient();
        var accessToken = await RequestAccessTokenAsync(
            enrollment,
            tokenHttpClient,
            cancellationToken);
        app.Logger.LogInformation(
            "Sending CloudShell Device Registry heartbeat for device {DeviceId}.",
            enrollment.DeviceId);
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
        app.Logger.LogInformation(
            "CloudShell Device Registry heartbeat completed for device {DeviceId} with presence {Presence}.",
            heartbeat.DeviceId,
            heartbeat.Presence);
        app.Logger.LogInformation(
            "Synchronizing CloudShell Device Registry twin state for device {DeviceId}.",
            enrollment.DeviceId);
        sync = await client.SyncDeviceAsync(
            registryResourceId,
            enrollment.DeviceId,
            accessToken,
            new DeviceSyncRequest(
                new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mode"] = JsonSerializer.SerializeToElement("running"),
                    ["configurationSetting"] = JsonSerializer.SerializeToElement(configurationSettingName)
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sample.sync"] = "device-app"
                },
                "sample-app",
                LastKnownDesiredVersion: 0),
            cancellationToken);
        app.Logger.LogInformation(
            "CloudShell Device Registry twin sync completed for device {DeviceId}; desired version {DesiredVersion}, reported version {ReportedVersion}, changed {DesiredStateChanged}.",
            sync.Device.DeviceId,
            sync.Desired.Version,
            sync.Reported.Version,
            sync.DesiredStateChanged);
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
        app.Logger.LogInformation(
            "Reading CloudShell Configuration Store setting {SettingName} for device {DeviceId}.",
            configurationSettingName,
            enrollment.DeviceId);
        configuration = await configurationClient.GetSettingAsync(
            configurationSettingName,
            cancellationToken);
        app.Logger.LogInformation(
            "CloudShell Configuration Store setting {SettingName} was read for device {DeviceId}.",
            configuration?.Name ?? configurationSettingName,
            enrollment.DeviceId);
    }

    if (!string.IsNullOrWhiteSpace(registryMqttEndpoint))
    {
        var mqttClient = new DeviceRegistryMqttClient(new Uri(registryMqttEndpoint));
        app.Logger.LogInformation(
            "Synchronizing CloudShell Device Registry twin state over MQTT for device {DeviceId}.",
            enrollment.DeviceId);
        mqttSync = await mqttClient.SyncDeviceAsync(
            registryResourceId,
            enrollment.DeviceId,
            enrollment.ClientId,
            enrollment.ClientSecret,
            new DeviceSyncRequest(
                new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mode"] = JsonSerializer.SerializeToElement("running"),
                    ["transport"] = JsonSerializer.SerializeToElement("mqtt")
                },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sample.mqttSync"] = "device-app"
                },
                "sample-app-mqtt",
                sync?.Desired.Version),
            cancellationToken: cancellationToken);
        mqttSyncPublished = true;
        app.Logger.LogInformation(
            "CloudShell Device Registry MQTT twin sync completed for device {DeviceId}; desired state version {DesiredVersion}, changed {DesiredStateChanged}.",
            enrollment.DeviceId,
            mqttSync.Desired.Version,
            mqttSync.DesiredStateChanged);
    }

    if (!string.IsNullOrWhiteSpace(eventBrokerEndpoint))
    {
        var eventClient = new EventBrokerClient(new Uri(eventBrokerEndpoint));
        app.Logger.LogInformation(
            "Publishing CloudShell Event Broker check-in event for device {DeviceId} to stream {Stream}.",
            enrollment.DeviceId,
            eventBrokerStream);
        publishedEvent = await eventClient.PublishAsync(
            eventBrokerResourceId,
            eventBrokerStream,
            new EventBrokerPublishRequest(
                "cloudshell.device.checkin",
                JsonSerializer.SerializeToElement(new
                {
                    enrollment.DeviceId,
                    enrollment.Subject,
                    heartbeat?.Presence,
                    configurationSetting = configuration?.Name,
                    configurationValue = configuration?.Value,
                    mqttSyncPublished
                }),
                Source: "samples/device-registry/device-app",
                Subject: enrollment.Subject,
                Properties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["deviceId"] = enrollment.DeviceId,
                    ["registryResourceId"] = registryResourceId
                }),
            cancellationToken);
        app.Logger.LogInformation(
            "CloudShell Event Broker retained event {EventId} at sequence {Sequence} for device {DeviceId}.",
            publishedEvent.Id,
            publishedEvent.Sequence,
            enrollment.DeviceId);
    }

    return Results.Ok(new
    {
        enrollment,
        heartbeat,
        sync,
        mqttSync,
        publishedEvent,
        mqttSyncPublished,
        configuration
    });
});

app.Run();

static Uri BuildConfigurationSettingsEndpoint(
    string configurationEndpoint,
    string configurationResourceId) =>
    new($"{configurationEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(configurationResourceId)}/settings");

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
