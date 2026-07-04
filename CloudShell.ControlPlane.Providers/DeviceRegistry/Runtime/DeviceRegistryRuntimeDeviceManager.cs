using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public interface IDeviceRegistryRuntimeDeviceManager
{
    ValueTask<IReadOnlyList<DeviceRegistryRuntimeDevice>> ListDevicesAsync(
        string resourceId,
        CancellationToken cancellationToken = default);
}

public sealed class DeviceRegistryRuntimeDeviceManager(
    DeviceRegistryRuntimeOptions options) : IDeviceRegistryRuntimeDeviceManager
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly DeviceRegistryRuntimeOptions _options = options;

    public ValueTask<IReadOnlyList<DeviceRegistryRuntimeDevice>> ListDevicesAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        cancellationToken.ThrowIfCancellationRequested();

        var devicesPath = Path.Combine(
            _options.DefinitionsDirectory,
            SanitizeFileName(resourceId),
            "device-registries.devices.json");
        if (!File.Exists(devicesPath))
        {
            return ValueTask.FromResult<IReadOnlyList<DeviceRegistryRuntimeDevice>>([]);
        }

        using var stream = File.OpenRead(devicesPath);
        var devices = JsonSerializer.Deserialize<List<DeviceRegistryRuntimeDevice>>(
            stream,
            SerializerOptions) ?? [];
        return ValueTask.FromResult<IReadOnlyList<DeviceRegistryRuntimeDevice>>(
            devices
                .Where(device => string.Equals(
                    device.RegistryId,
                    resourceId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(device => device.Subject, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalid.Contains(character) || character is ':' or '/' or '\\'
                ? '_'
                : character));
    }
}

public sealed record DeviceRegistryRuntimeDevice(
    string Id,
    string RegistryId,
    string Subject,
    string IdentityCategory,
    string IdentityProviderId,
    string IdentityResourceId,
    string IdentityName,
    string ClientId,
    IReadOnlyDictionary<string, string> Claims,
    IReadOnlyDictionary<string, string> Properties,
    DateTimeOffset EnrolledAt);
