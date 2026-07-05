using System.Text;
using System.Text.Json;
using System.Threading.Channels;
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
    private const string ServerClientId = "cloudshell-device-registry";
    private const string MqttTransport = "mqtt";
    private readonly DeviceRegistryServiceOptions _options = options.Value;
    private readonly Channel<PendingMqttMessage> _outboundMessages =
        Channel.CreateUnbounded<PendingMqttMessage>(
            new UnboundedChannelOptions
            {
                SingleReader = true
            });
    private CancellationTokenSource? _publisherCancellation;
    private Task? _publisherTask;
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
        _publisherCancellation = new CancellationTokenSource();
        _publisherTask = PublishOutboundMessagesAsync(_publisherCancellation.Token);
        logger.LogInformation(
            "CloudShell Device Registry MQTT endpoint is listening on {MqttEndpoint}.",
            _options.MqttEndpoint);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_publisherCancellation is not null)
        {
            await _publisherCancellation.CancelAsync();
        }

        if (_server is null)
        {
            return;
        }

        await _server.StopAsync();
        _server = null;
        if (_publisherTask is not null)
        {
            try
            {
                await _publisherTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _publisherCancellation?.Dispose();
        _publisherCancellation = null;
        _publisherTask = null;
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
        if (string.Equals(args.ClientId, ServerClientId, StringComparison.Ordinal))
        {
            args.ProcessPublish = true;
            return;
        }

        args.ProcessPublish = false;
        if (!args.SessionItems.Contains(SessionDeviceClientIdKey) ||
            args.SessionItems[SessionDeviceClientIdKey] is not string clientId)
        {
            RejectPublish(
                args,
                MqttPubAckReasonCode.NotAuthorized,
                "Device MQTT session is not authenticated.");
            return;
        }

        var topic = DeviceRegistryMqttTopics.Parse(args.ApplicationMessage.Topic);
        if (topic is null)
        {
            RejectPublish(
                args,
                MqttPubAckReasonCode.TopicNameInvalid,
                "Device Registry MQTT topic is not supported.");
            return;
        }

        var registry = store.GetRegistry(topic.RegistryId);
        if (registry is null)
        {
            RejectPublish(
                args,
                MqttPubAckReasonCode.TopicNameInvalid,
                "Device Registry was not found.");
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
                        timestamp,
                        MqttTransport);
                    if (!heartbeatResult.IsAccepted)
                    {
                        RejectPublish(
                            args,
                            heartbeatResult.IsNotFound
                                ? MqttPubAckReasonCode.TopicNameInvalid
                                : MqttPubAckReasonCode.NotAuthorized,
                            heartbeatResult.Failure ?? "Device heartbeat was rejected.");
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
                        timestamp,
                        MqttTransport);
                    if (!syncResult.IsAccepted)
                    {
                        RejectPublish(
                            args,
                            syncResult.IsNotFound
                                ? MqttPubAckReasonCode.TopicNameInvalid
                                : MqttPubAckReasonCode.NotAuthorized,
                            syncResult.Failure ?? "Device sync was rejected.");
                        break;
                    }

                    EnqueueSyncResponse(args.ApplicationMessage.ResponseTopic, registry, syncResult, timestamp);
                    break;
            }
        }
        catch (JsonException exception)
        {
            RejectPublish(
                args,
                MqttPubAckReasonCode.PayloadFormatInvalid,
                $"Invalid Device Registry MQTT payload. {exception.Message}");
        }
    }

    private void EnqueueSyncResponse(
        string? responseTopic,
        DeviceRegistryDefinition registry,
        DeviceSyncMutationResult syncResult,
        DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(responseTopic) ||
            !syncResult.IsAccepted ||
            syncResult.Device is null)
        {
            return;
        }

        var response = new DeviceSyncResponse(
            ToMetadataResponse(registry, syncResult.Device, timestamp),
            syncResult.Device.Twin.Desired,
            syncResult.Device.Twin.Reported,
            syncResult.DesiredStateChanged,
            syncResult.Device.Twin.LastSyncedAt);
        _outboundMessages.Writer.TryWrite(
            new PendingMqttMessage(
                responseTopic,
                JsonSerializer.Serialize(response, DeviceRegistryMqttJson.SerializerOptions)));
    }

    private async Task PublishOutboundMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _outboundMessages.Reader.ReadAllAsync(cancellationToken))
            {
                var server = _server;
                if (server is null)
                {
                    continue;
                }

                await server.InjectApplicationMessage(
                    new InjectedMqttApplicationMessage(
                        new MqttApplicationMessageBuilder()
                            .WithTopic(message.Topic)
                            .WithPayload(message.Payload)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                            .Build())
                    {
                        SenderClientId = ServerClientId
                    },
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Device Registry MQTT response publisher stopped unexpectedly.");
        }
    }

    private DeviceMetadataResponse ToMetadataResponse(
        DeviceRegistryDefinition registry,
        DeviceRecord device,
        DateTimeOffset timestamp) =>
        new(
            device.Id,
            device.Subject,
            device.IdentityCategory,
            store.CreatePrincipal(device),
            device.IdentityProviderId,
            device.IdentityResourceId,
            device.IdentityName,
            device.ClientId,
            device.Claims,
            device.Properties,
            device.EnrolledAt,
            device.Status,
            device.LastSeenAt,
            device.LastSeenSource,
            device.RevokedAt,
            device.RevokedReason,
            ResolvePresence(registry, device, timestamp),
            device.EnrollmentProfileName,
            device.EnrollmentProfileKind,
            device.LastSeenTransport);

    private static string ResolvePresence(
        DeviceRegistryDefinition registry,
        DeviceRecord device,
        DateTimeOffset timestamp)
    {
        if (string.Equals(device.Status, DeviceRecordStatuses.Revoked, StringComparison.OrdinalIgnoreCase) ||
            device.RevokedAt is not null)
        {
            return DevicePresenceStatuses.Revoked;
        }

        if (device.LastSeenAt is null)
        {
            return DevicePresenceStatuses.Unknown;
        }

        return registry.HeartbeatStaleAfterSeconds is > 0 &&
            timestamp - device.LastSeenAt.Value > TimeSpan.FromSeconds(registry.HeartbeatStaleAfterSeconds.Value)
                ? DevicePresenceStatuses.Stale
                : DevicePresenceStatuses.Online;
    }

    private static void RejectPublish(
        InterceptingPublishEventArgs args,
        MqttPubAckReasonCode reasonCode,
        string reason)
    {
        args.ProcessPublish = false;
        args.Response.ReasonCode = reasonCode;
        args.Response.ReasonString = reason;
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

internal sealed record PendingMqttMessage(
    string Topic,
    string Payload);

internal static class DeviceRegistryMqttJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);
}
