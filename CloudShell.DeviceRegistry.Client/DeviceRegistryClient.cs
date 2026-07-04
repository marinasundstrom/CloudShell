using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CloudShell.DeviceRegistry.Client;

/// <summary>
/// SDK client for CloudShell Device Registry enrollment.
/// </summary>
/// <remarks>
/// Public preview API. Specialized device clients can layer richer device
/// discovery over this generic subject-and-claims enrollment contract.
/// </remarks>
public sealed class DeviceRegistryClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    public DeviceRegistryClient(Uri registryEndpoint)
        : this(registryEndpoint, new HttpClient())
    {
    }

    public DeviceRegistryClient(
        Uri registryEndpoint,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(registryEndpoint);
        ArgumentNullException.ThrowIfNull(httpClient);

        RegistryEndpoint = registryEndpoint;
        this.httpClient = httpClient;
    }

    public Uri RegistryEndpoint { get; }

    public static DeviceRegistryClient FromEnvironment(
        string? registryName = null) =>
        TryCreateFromEnvironment(registryName) ??
        throw new InvalidOperationException(
            "No CloudShell Device Registry endpoint was found in the environment.");

    public static DeviceRegistryClient? TryCreateFromEnvironment(
        string? registryName = null)
    {
        var endpoint = FindEndpoint(registryName);
        return endpoint is null
            ? null
            : new DeviceRegistryClient(endpoint);
    }

    public Task<DeviceEnrollmentResponse> EnrollCurrentDeviceAsync(
        string registryId,
        IReadOnlyDictionary<string, string>? claims = null,
        IReadOnlyDictionary<string, string>? properties = null,
        CancellationToken cancellationToken = default) =>
        EnrollDeviceAsync(
            registryId,
            CreateCurrentDeviceSubject(),
            claims,
            MergeCurrentDeviceProperties(properties),
            cancellationToken);

    public async Task<DeviceEnrollmentResponse> EnrollDeviceAsync(
        string registryId,
        string subject,
        IReadOnlyDictionary<string, string>? claims = null,
        IReadOnlyDictionary<string, string>? properties = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        using var response = await httpClient.PostAsJsonAsync(
            BuildEnrollmentEndpoint(registryId),
            new DeviceEnrollmentRequest(subject, claims, properties),
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<DeviceEnrollmentResponse>(
            SerializerOptions,
            cancellationToken) ??
            throw new JsonException("CloudShell Device Registry returned an empty enrollment response.");
    }

    public async Task<IReadOnlyList<DeviceMetadataResponse>> GetDevicesAsync(
        string registryId,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildDevicesEndpoint(registryId));
        request.Headers.Authorization = new("Bearer", bearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<DeviceMetadataResponse>>(
            SerializerOptions,
            cancellationToken) ?? [];
    }

    private Uri BuildEnrollmentEndpoint(string registryId)
    {
        var builder = new UriBuilder(RegistryEndpoint);
        var path = builder.Path.TrimEnd('/');
        builder.Path = $"{path}/api/devices/registries/{Uri.EscapeDataString(registryId)}/enroll";
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private Uri BuildDevicesEndpoint(string registryId)
    {
        var builder = new UriBuilder(RegistryEndpoint);
        var path = builder.Path.TrimEnd('/');
        builder.Path = $"{path}/api/devices/registries/{Uri.EscapeDataString(registryId)}/devices";
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private static string CreateCurrentDeviceSubject()
    {
        var machineName = Environment.MachineName;
        if (string.IsNullOrWhiteSpace(machineName))
        {
            machineName = "current";
        }

        var characters = machineName
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        var normalized = new string(characters).Trim('-');

        return $"device/{(string.IsNullOrWhiteSpace(normalized) ? "current" : normalized)}";
    }

    private static IReadOnlyDictionary<string, string> MergeCurrentDeviceProperties(
        IReadOnlyDictionary<string, string>? properties)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["platform"] = GetPlatform(),
            ["operatingSystem"] = Environment.OSVersion.Platform.ToString(),
            ["machineName"] = Environment.MachineName,
            ["osDescription"] = RuntimeInformation.OSDescription,
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["frameworkDescription"] = RuntimeInformation.FrameworkDescription,
            ["processorCount"] = Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (properties is not null)
        {
            foreach (var (name, value) in properties)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    merged[name.Trim()] = value;
                }
            }
        }

        return merged;
    }

    private static string GetPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        if (OperatingSystem.IsAndroid())
        {
            return "android";
        }

        if (OperatingSystem.IsIOS())
        {
            return "ios";
        }

        if (OperatingSystem.IsBrowser())
        {
            return "browser";
        }

        return "unknown";
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail)
                ? $"CloudShell Device Registry returned {(int)response.StatusCode}."
                : $"CloudShell Device Registry returned {(int)response.StatusCode}. {detail}",
            null,
            response.StatusCode);
    }

    private static Uri? FindEndpoint(string? registryName)
    {
        var variables = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string && entry.Value is string)
            .ToDictionary(
                entry => (string)entry.Key,
                entry => (string)entry.Value!,
                StringComparer.OrdinalIgnoreCase);

        foreach (var (_, endpoint) in variables
            .Where(item =>
                item.Key.StartsWith("CLOUDSHELL_DEVICE_REGISTRY_", StringComparison.OrdinalIgnoreCase) &&
                item.Key.EndsWith("_ENDPOINT", StringComparison.OrdinalIgnoreCase) &&
                MatchesRegistryName(item.Key, registryName))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static bool MatchesRegistryName(string environmentVariableName, string? registryName)
    {
        if (string.IsNullOrWhiteSpace(registryName))
        {
            return true;
        }

        var normalized = NormalizeEnvironmentSegment(registryName);
        return environmentVariableName.Contains(
            $"CLOUDSHELL_DEVICE_REGISTRY_{normalized}_",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEnvironmentSegment(string value)
    {
        var characters = value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_')
            .ToArray();

        return new string(characters).Trim('_');
    }
}
