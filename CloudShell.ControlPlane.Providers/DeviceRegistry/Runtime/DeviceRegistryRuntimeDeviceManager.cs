using System.Net.Http.Json;
using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public interface IDeviceRegistryRuntimeDeviceManager
{
    ValueTask<IReadOnlyList<DeviceRegistryRuntimeDevice>> ListDevicesAsync(
        CloudShell.Abstractions.ResourceManager.Resource resource,
        CancellationToken cancellationToken = default);

    ValueTask<DeviceRegistryRuntimeDevice> RevokeDeviceAsync(
        CloudShell.Abstractions.ResourceManager.Resource resource,
        string deviceId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    ValueTask RemoveDeviceAsync(
        CloudShell.Abstractions.ResourceManager.Resource resource,
        string deviceId,
        CancellationToken cancellationToken = default);

    ValueTask<DeviceRegistryRuntimeDeviceTwin> SetDesiredStateAsync(
        CloudShell.Abstractions.ResourceManager.Resource resource,
        string deviceId,
        IReadOnlyDictionary<string, JsonElement> state,
        CancellationToken cancellationToken = default);
}

public sealed class DeviceRegistryRuntimeDeviceManager(
    DeviceRegistryRuntimeOptions options) : IDeviceRegistryRuntimeDeviceManager
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly DeviceRegistryRuntimeOptions _options = options;

    public ValueTask<IReadOnlyList<DeviceRegistryRuntimeDevice>> ListDevicesAsync(
        CloudShell.Abstractions.ResourceManager.Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        cancellationToken.ThrowIfCancellationRequested();

        var devicesPath = Path.Combine(
            _options.DefinitionsDirectory,
            SanitizeFileName(resource.Id),
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
                    resource.Id,
                    StringComparison.OrdinalIgnoreCase))
                .Select(device => device with
                {
                    Presence = ResolvePresence(
                        device,
                        GetHeartbeatStaleAfter(resource),
                        DateTimeOffset.UtcNow)
                })
                .OrderBy(device => device.Subject, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public async ValueTask<DeviceRegistryRuntimeDevice> RevokeDeviceAsync(
        CloudShell.Abstractions.ResourceManager.Resource resource,
        string deviceId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var endpoint = GetEndpoint(resource);
        var token = await RequestManagementTokenAsync(endpoint, cancellationToken);
        using var client = CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildDeviceActionEndpoint(endpoint, resource.Id, deviceId, "revoke"))
        {
            Content = JsonContent.Create(new { reason }, options: SerializerOptions)
        };
        request.Headers.Authorization = new("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var device = await response.Content.ReadFromJsonAsync<DeviceRegistryRuntimeDeviceResponse>(
            SerializerOptions,
            cancellationToken) ??
            throw new JsonException("Device Registry returned an empty revoke response.");
        return device.ToRuntimeDevice();
    }

    public async ValueTask RemoveDeviceAsync(
        CloudShell.Abstractions.ResourceManager.Resource resource,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var endpoint = GetEndpoint(resource);
        var token = await RequestManagementTokenAsync(endpoint, cancellationToken);
        using var client = CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            BuildDeviceEndpoint(endpoint, resource.Id, deviceId));
        request.Headers.Authorization = new("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async ValueTask<DeviceRegistryRuntimeDeviceTwin> SetDesiredStateAsync(
        CloudShell.Abstractions.ResourceManager.Resource resource,
        string deviceId,
        IReadOnlyDictionary<string, JsonElement> state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(state);

        var endpoint = GetEndpoint(resource);
        var token = await RequestManagementTokenAsync(endpoint, cancellationToken);
        using var client = CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            BuildDeviceDesiredStateEndpoint(endpoint, resource.Id, deviceId))
        {
            Content = JsonContent.Create(new { state }, options: SerializerOptions)
        };
        request.Headers.Authorization = new("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<DeviceRegistryRuntimeDeviceTwin>(
            SerializerOptions,
            cancellationToken) ??
            throw new JsonException("Device Registry returned an empty device twin response.");
    }

    private async Task<string> RequestManagementTokenAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(
            $"{endpoint.TrimEnd('/')}/api/auth/v1/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ManagementClientId,
                ["client_secret"] = _options.ManagementClientSecret,
                ["scope"] = "ControlPlane.Access"
            }),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("access_token").GetString() ??
            throw new JsonException("Device Registry token response did not include an access token.");
    }

    private static string GetEndpoint(
        CloudShell.Abstractions.ResourceManager.Resource resource)
    {
        var endpoint = resource.ResourceAttributes.GetValueOrDefault(
            DeviceRegistryResourceTypeProvider.Attributes.Endpoint.ToString());
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("Device Registry endpoint is required before devices can be managed.");
        }

        return endpoint;
    }

    private static HttpClient CreateClient() =>
        new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

    private static int? GetHeartbeatStaleAfter(
        CloudShell.Abstractions.ResourceManager.Resource resource) =>
        int.TryParse(
            resource.ResourceAttributes.GetValueOrDefault(
                DeviceRegistryResourceTypeProvider.Attributes.HeartbeatStaleAfterSeconds.ToString()),
            out var seconds)
                ? seconds
                : null;

    private static string ResolvePresence(
        DeviceRegistryRuntimeDevice device,
        int? staleAfterSeconds,
        DateTimeOffset timestamp)
    {
        if (string.Equals(device.Status, "revoked", StringComparison.OrdinalIgnoreCase) ||
            device.RevokedAt is not null)
        {
            return "revoked";
        }

        if (device.LastSeenAt is null)
        {
            return "unknown";
        }

        return staleAfterSeconds is > 0 &&
            timestamp - device.LastSeenAt.Value > TimeSpan.FromSeconds(staleAfterSeconds.Value)
                ? "stale"
                : "online";
    }

    private static Uri BuildDeviceEndpoint(
        string endpoint,
        string registryId,
        string deviceId)
    {
        var builder = new UriBuilder(endpoint);
        var path = builder.Path.TrimEnd('/');
        builder.Path = $"{path}/api/devices/registries/{Uri.EscapeDataString(registryId)}/devices/{Uri.EscapeDataString(deviceId)}";
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private static Uri BuildDeviceActionEndpoint(
        string endpoint,
        string registryId,
        string deviceId,
        string action)
    {
        var builder = new UriBuilder(BuildDeviceEndpoint(endpoint, registryId, deviceId));
        builder.Path = $"{builder.Path.TrimEnd('/')}/{action}";
        return builder.Uri;
    }

    private static Uri BuildDeviceDesiredStateEndpoint(
        string endpoint,
        string registryId,
        string deviceId)
    {
        var builder = new UriBuilder(BuildDeviceEndpoint(endpoint, registryId, deviceId));
        builder.Path = $"{builder.Path.TrimEnd('/')}/twin/desired";
        return builder.Uri;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Device Registry returned {(int)response.StatusCode} {response.ReasonPhrase}. {content}".Trim());
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
    DateTimeOffset EnrolledAt)
{
    public string Status { get; init; } = "active";

    public DateTimeOffset? LastSeenAt { get; init; }

    public string? LastSeenSource { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public string? RevokedReason { get; init; }

    public string? Presence { get; init; }

    public DeviceRegistryRuntimeDeviceTwin Twin { get; init; } = new();
}

public sealed record DeviceRegistryRuntimeDeviceTwin
{
    public DeviceRegistryRuntimeDeviceTwinState Desired { get; init; } = new();

    public DeviceRegistryRuntimeDeviceTwinState Reported { get; init; } = new();

    public DateTimeOffset? LastSyncedAt { get; init; }
}

public sealed record DeviceRegistryRuntimeDeviceTwinState
{
    public long Version { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public IReadOnlyDictionary<string, JsonElement> State { get; init; } =
        new Dictionary<string, JsonElement>();
}

internal sealed record DeviceRegistryRuntimeDeviceResponse(
    string DeviceId,
    string Subject,
    string IdentityCategory,
    CloudShell.Abstractions.ResourceManager.ResourcePrincipalReference Principal,
    string IdentityProviderId,
    string IdentityResourceId,
    string IdentityName,
    string ClientId,
    IReadOnlyDictionary<string, string> Claims,
    IReadOnlyDictionary<string, string> Properties,
    DateTimeOffset EnrolledAt,
    string Status,
    DateTimeOffset? LastSeenAt,
    string? LastSeenSource,
    DateTimeOffset? RevokedAt,
    string? RevokedReason,
    string? Presence = null,
    DeviceRegistryRuntimeDeviceTwin? Twin = null)
{
    public DeviceRegistryRuntimeDevice ToRuntimeDevice() =>
        new(
            DeviceId,
            IdentityResourceId,
            Subject,
            IdentityCategory,
            IdentityProviderId,
            IdentityResourceId,
            IdentityName,
            ClientId,
            Claims,
            Properties,
            EnrolledAt)
        {
            Status = Status,
            LastSeenAt = LastSeenAt,
            LastSeenSource = LastSeenSource,
            RevokedAt = RevokedAt,
            RevokedReason = RevokedReason,
            Presence = Presence,
            Twin = Twin ?? new()
        };
}
