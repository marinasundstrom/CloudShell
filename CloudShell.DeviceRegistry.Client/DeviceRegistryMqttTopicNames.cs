namespace CloudShell.DeviceRegistry.Client;

public static class DeviceRegistryMqttTopicNames
{
    public static string BuildHeartbeatTopic(
        string registryId,
        string deviceId) =>
        BuildDeviceOperationTopic(registryId, deviceId, "heartbeat");

    public static string BuildSyncTopic(
        string registryId,
        string deviceId) =>
        BuildDeviceOperationTopic(registryId, deviceId, "sync");

    public static string BuildResponseTopic(
        string registryId,
        string deviceId,
        string requestId) =>
        $"cloudshell/device-registries/{Escape(registryId)}/devices/{Escape(deviceId)}/responses/{Escape(requestId)}";

    private static string BuildDeviceOperationTopic(
        string registryId,
        string deviceId,
        string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        return $"cloudshell/device-registries/{Escape(registryId)}/devices/{Escape(deviceId)}/{operation}";
    }

    private static string Escape(string value) =>
        Uri.EscapeDataString(value.Trim());
}
