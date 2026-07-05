using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace CloudShell.DeviceRegistry.Client;

/// <summary>
/// Experimental MQTT transport client for CloudShell Device Registry heartbeat
/// and twin sync messages.
/// </summary>
public sealed class DeviceRegistryMqttClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly Uri endpoint;

    public DeviceRegistryMqttClient(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!string.Equals(endpoint.Scheme, "mqtt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Device Registry MQTT endpoints must use the mqtt:// scheme.",
                nameof(endpoint));
        }

        this.endpoint = endpoint;
    }

    public async Task SendHeartbeatAsync(
        string registryId,
        string deviceId,
        string clientId,
        string clientSecret,
        IReadOnlyDictionary<string, string>? properties = null,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);

        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        await client.ConnectAsync(
            CreateOptions(deviceId, clientId, clientSecret),
            cancellationToken);

        var publishResult = await client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(DeviceRegistryMqttTopicNames.BuildHeartbeatTopic(registryId, deviceId))
                .WithPayload(JsonSerializer.Serialize(
                    new DeviceHeartbeatRequest(properties, source),
                    SerializerOptions))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(),
            cancellationToken);
        EnsurePublishSuccess(publishResult);
        await client.DisconnectAsync(new(), cancellationToken);
    }

    public async Task PublishSyncAsync(
        string registryId,
        string deviceId,
        string clientId,
        string clientSecret,
        DeviceSyncRequest sync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ArgumentNullException.ThrowIfNull(sync);

        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        await client.ConnectAsync(
            CreateOptions(deviceId, clientId, clientSecret),
            cancellationToken);

        var publishResult = await client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(DeviceRegistryMqttTopicNames.BuildSyncTopic(registryId, deviceId))
                .WithPayload(JsonSerializer.Serialize(sync, SerializerOptions))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(),
            cancellationToken);
        EnsurePublishSuccess(publishResult);
        await client.DisconnectAsync(new(), cancellationToken);
    }

    private MqttClientOptions CreateOptions(
        string deviceId,
        string clientId,
        string clientSecret) =>
        new MqttClientOptionsBuilder()
            .WithTcpServer(endpoint.Host, endpoint.Port > 0 ? endpoint.Port : 1883)
            .WithClientId(CreateClientIdentifier(deviceId))
            .WithCredentials(clientId, clientSecret)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanSession()
            .Build();

    private static string CreateClientIdentifier(string deviceId)
    {
        var characters = deviceId
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        return $"cloudshell-device-{new string(characters).Trim('-')}";
    }

    private static void EnsurePublishSuccess(MqttClientPublishResult result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.ReasonString)
                    ? $"CloudShell Device Registry MQTT publish failed with reason {result.ReasonCode}."
                    : $"CloudShell Device Registry MQTT publish failed with reason {result.ReasonCode}: {result.ReasonString}");
        }
    }
}
