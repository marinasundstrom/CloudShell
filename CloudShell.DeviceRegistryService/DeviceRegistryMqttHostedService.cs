using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace CloudShell.DeviceRegistryService;

public sealed class DeviceRegistryMqttHostedService(
    IOptions<DeviceRegistryServiceOptions> options,
    DeviceRegistryServiceStore store,
    ILogger<DeviceRegistryMqttHostedService> logger) : IHostedService
{
    private const string SessionDeviceClientIdKey = "cloudshell.device.clientId";
    private readonly DeviceRegistryServiceOptions _options = options.Value;
    private MqttServer? _server;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!TryGetMqttPort(_options.MqttEndpoint, out var port))
        {
            logger.LogDebug("Device Registry MQTT endpoint is not configured.");
            return;
        }

        var factory = new MqttFactory();
        var serverOptions = factory
            .CreateServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();

        _server = factory.CreateMqttServer(serverOptions);
        _server.ValidatingConnectionAsync += ValidateConnectionAsync;
        _server.InterceptingPublishAsync += InterceptPublishAsync;

        await _server.StartAsync();
        logger.LogInformation(
            "CloudShell Device Registry MQTT endpoint is listening on {MqttEndpoint}.",
            _options.MqttEndpoint);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is null)
        {
            return;
        }

        await _server.StopAsync();
        _server = null;
    }

    private Task ValidateConnectionAsync(ValidatingConnectionEventArgs args)
    {
        if (!store.TryValidateDeviceCredentials(
                args.UserName,
                args.Password,
                out var device))
        {
            args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            args.ReasonString = "Device identity credentials are invalid.";
            return Task.CompletedTask;
        }

        args.SessionItems[SessionDeviceClientIdKey] = device.ClientId;
        args.ReasonCode = MqttConnectReasonCode.Success;
        return Task.CompletedTask;
    }

    private async Task InterceptPublishAsync(InterceptingPublishEventArgs args)
    {
        args.ProcessPublish = false;
        if (!args.SessionItems.Contains(SessionDeviceClientIdKey) ||
            args.SessionItems[SessionDeviceClientIdKey] is not string clientId)
        {
            args.Response.ReasonString = "Device MQTT session is not authenticated.";
            return;
        }

        var topic = DeviceRegistryMqttTopics.Parse(args.ApplicationMessage.Topic);
        if (topic is null)
        {
            args.ProcessPublish = true;
            return;
        }

        var registry = store.GetRegistry(topic.RegistryId);
        if (registry is null)
        {
            args.Response.ReasonString = "Device Registry was not found.";
            return;
        }

        try
        {
            var payload = GetPayload(args.ApplicationMessage);
            var timestamp = DateTimeOffset.UtcNow;
            switch (topic.Operation)
            {
                case DeviceRegistryMqttOperations.Heartbeat:
                    var heartbeat = JsonSerializer.Deserialize<DeviceHeartbeatRequest>(
                        payload,
                        DeviceRegistryMqttJson.SerializerOptions) ?? new();
                    var heartbeatResult = store.RecordHeartbeat(
                        registry.Id,
                        topic.DeviceId,
                        clientId,
                        heartbeat,
                        timestamp);
                    if (!heartbeatResult.IsAccepted)
                    {
                        args.Response.ReasonString = heartbeatResult.Failure;
                    }

                    break;

                case DeviceRegistryMqttOperations.Sync:
                    var sync = JsonSerializer.Deserialize<DeviceSyncRequest>(
                        payload,
                        DeviceRegistryMqttJson.SerializerOptions) ?? new();
                    var syncResult = store.SyncDevice(
                        registry.Id,
                        topic.DeviceId,
                        clientId,
                        sync,
                        timestamp);
                    if (!syncResult.IsAccepted)
                    {
                        args.Response.ReasonString = syncResult.Failure;
                        break;
                    }

                    break;
            }
        }
        catch (JsonException exception)
        {
            args.Response.ReasonString = $"Invalid Device Registry MQTT payload. {exception.Message}";
        }
    }

    private static string GetPayload(MqttApplicationMessage message)
    {
        var segment = message.PayloadSegment;
        if (segment.Array is not null)
        {
            return Encoding.UTF8.GetString(segment.Array, segment.Offset, segment.Count);
        }

        return string.Empty;
    }

    private static bool TryGetMqttPort(
        string? endpoint,
        out int port)
    {
        port = 0;
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, "mqtt", StringComparison.OrdinalIgnoreCase) &&
            uri.Port is > 0 and <= 65535 &&
            (port = uri.Port) > 0;
    }
}

internal static class DeviceRegistryMqttOperations
{
    public const string Heartbeat = "heartbeat";
    public const string Sync = "sync";
}

internal static class DeviceRegistryMqttTopics
{
    public static DeviceRegistryMqttTopic? Parse(string? topic)
    {
        var segments = topic?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments is not
            [
                "cloudshell",
                "device-registries",
                var registryId,
                "devices",
                var deviceId,
                var operation
            ])
        {
            return null;
        }

        if (!string.Equals(operation, DeviceRegistryMqttOperations.Heartbeat, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(operation, DeviceRegistryMqttOperations.Sync, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new(
            Uri.UnescapeDataString(registryId),
            Uri.UnescapeDataString(deviceId),
            operation.ToLowerInvariant());
    }
}

internal sealed record DeviceRegistryMqttTopic(
    string RegistryId,
    string DeviceId,
    string Operation);

internal static class DeviceRegistryMqttJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);
}
