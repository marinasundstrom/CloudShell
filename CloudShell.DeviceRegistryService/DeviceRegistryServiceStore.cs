using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using Microsoft.Extensions.Options;

namespace CloudShell.DeviceRegistryService;

public sealed class DeviceRegistryServiceStore(
    IOptions<DeviceRegistryServiceOptions> options,
    BuiltInResourceIdentityRegistry identities,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _definitionsPath = ResolvePath(
        options.Value.DefinitionsPath,
        environment.ContentRootPath);
    private readonly string _devicesPath = ResolveDevicesPath(
        options.Value.DevicesPath,
        options.Value.DefinitionsPath,
        environment.ContentRootPath);
    private readonly string? _resourceId = string.IsNullOrWhiteSpace(options.Value.ResourceId)
        ? null
        : options.Value.ResourceId.Trim();
    private const string DeviceIdentityCategory = "deviceIdentity";
    private readonly ResourceIdentityProviderDefinition _identityProvider = new(
        "built-in",
        "Built-in device identities",
        ResourceIdentityProviderKind.BuiltIn);

    public void RehydrateDeviceIdentities()
    {
        foreach (var device in LoadDevices())
        {
            RegisterIdentity(device);
        }
    }

    public DeviceRegistryDefinition? GetRegistry(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId) ||
            (_resourceId is not null &&
             !string.Equals(_resourceId, resourceId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return LoadDefinitions().FirstOrDefault(registry =>
            string.Equals(registry.Id, resourceId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<DeviceRecord> ListDevices(string registryId)
    {
        lock (_gate)
        {
            return LoadDevices()
                .Where(device => string.Equals(device.RegistryId, registryId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(device => device.Subject, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public DeviceEnrollmentResult EnrollDevice(
        DeviceRegistryDefinition registry,
        DeviceEnrollmentRequest request,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(request);

        var normalizedSubject = request.Subject?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSubject))
        {
            return DeviceEnrollmentResult.Rejected("Device subject is required.");
        }

        var claims = NormalizeClaims(request.Claims);
        var properties = NormalizeProperties(request.Properties);
        var policyFailure = ValidatePolicy(registry.EnrollmentPolicy, normalizedSubject, claims);
        if (policyFailure is not null)
        {
            return DeviceEnrollmentResult.Rejected(policyFailure);
        }

        lock (_gate)
        {
            var devices = LoadDevices().ToList();
            var existing = devices.FirstOrDefault(device =>
                string.Equals(device.RegistryId, registry.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(device.Subject, normalizedSubject, StringComparison.OrdinalIgnoreCase));
            var deviceId = existing?.Id ?? CreateDeviceId(registry.Id, normalizedSubject);
            var identityName = deviceId;
            var identity = ResourceIdentityReference.ForResource(registry.Id, identityName);
            var binding = new ResourceIdentityBinding(
                _identityProvider.Id,
                Subject: normalizedSubject,
                Scopes: [BuiltInResourceIdentityRegistry.DefaultScope],
                Claims: claims,
                Name: identityName);
            var clientId = BuiltInResourceIdentityRegistry.CreateClientId(identity);
            var record = new DeviceRecord(
                deviceId,
                registry.Id,
                normalizedSubject,
                DeviceIdentityCategory,
                _identityProvider.Id,
                identity.ResourceId,
                identity.Name ?? identityName,
                clientId,
                claims,
                properties,
                existing?.EnrolledAt ?? timestamp);

            if (existing is null)
            {
                devices.Add(record);
            }
            else
            {
                devices[devices.IndexOf(existing)] = record;
            }

            WriteDevices(devices);
            identities.Register(
                _identityProvider,
                new ResourceIdentityProvisioningEntry(identity, binding),
                registry.PermissionGrants,
                CreatePrincipal(record));

            return DeviceEnrollmentResult.Enrolled(
                record,
                BuiltInResourceIdentityRegistry.ResolveClientSecret(_identityProvider, clientId));
        }
    }

    private void RegisterIdentity(DeviceRecord device)
    {
        var registry = GetRegistry(device.RegistryId);
        var identity = ResourceIdentityReference.ForResource(
            device.IdentityResourceId,
            device.IdentityName);
        var binding = new ResourceIdentityBinding(
            device.IdentityProviderId,
            Subject: device.Subject,
            Scopes: [BuiltInResourceIdentityRegistry.DefaultScope],
            Claims: device.Claims,
            Name: device.IdentityName);

        identities.Register(
            _identityProvider,
            new ResourceIdentityProvisioningEntry(identity, binding),
            registry?.PermissionGrants ?? [],
            CreatePrincipal(device));
    }

    public ResourcePrincipalReference CreatePrincipal(DeviceRecord device) =>
        ResourcePrincipalReference.ForDeviceIdentity(
            device.RegistryId,
            device.Id,
            device.Subject,
            device.IdentityProviderId);

    private IReadOnlyList<DeviceRegistryDefinition> LoadDefinitions()
    {
        if (!File.Exists(_definitionsPath))
        {
            return [];
        }

        using var stream = File.OpenRead(_definitionsPath);
        return JsonSerializer.Deserialize<List<DeviceRegistryDefinition>>(stream, SerializerOptions) ?? [];
    }

    private IReadOnlyList<DeviceRecord> LoadDevices()
    {
        if (!File.Exists(_devicesPath))
        {
            return [];
        }

        using var stream = File.OpenRead(_devicesPath);
        return JsonSerializer.Deserialize<List<DeviceRecord>>(stream, SerializerOptions) ?? [];
    }

    private void WriteDevices(IReadOnlyList<DeviceRecord> devices)
    {
        var directory = Path.GetDirectoryName(_devicesPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(_devicesPath);
        JsonSerializer.Serialize(stream, devices, SerializerOptions);
    }

    private static string? ValidatePolicy(
        DeviceRegistryEnrollmentPolicy policy,
        string subject,
        IReadOnlyDictionary<string, string> claims)
    {
        if (policy.SubjectPrefixes.Count > 0 &&
            !policy.SubjectPrefixes.Any(prefix =>
                subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return "Device subject is not allowed by the registry enrollment policy.";
        }

        foreach (var requiredClaim in policy.RequiredClaims)
        {
            if (!claims.TryGetValue(requiredClaim.Name, out var claimValue) ||
                !string.Equals(claimValue, requiredClaim.Value, StringComparison.Ordinal))
            {
                return $"Required enrollment claim '{requiredClaim.Name}' is missing or invalid.";
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> NormalizeClaims(
        IReadOnlyDictionary<string, string>? claims)
    {
        if (claims is null || claims.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in claims)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                normalized[name.Trim()] = value;
            }
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, string> NormalizeProperties(
        IReadOnlyDictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in properties)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                normalized[name.Trim()] = value;
            }
        }

        return normalized;
    }

    private static string CreateDeviceId(
        string registryId,
        string subject)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{registryId}\u001f{subject}"));
        return $"device-{Convert.ToHexString(bytes)[..24].ToLowerInvariant()}";
    }

    private static string ResolveDevicesPath(
        string? devicesPath,
        string definitionsPath,
        string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(devicesPath))
        {
            return ResolvePath(devicesPath, contentRootPath);
        }

        var resolvedDefinitionsPath = ResolvePath(definitionsPath, contentRootPath);
        var directory = Path.GetDirectoryName(resolvedDefinitionsPath) ?? contentRootPath;
        var fileName = Path.GetFileNameWithoutExtension(resolvedDefinitionsPath);
        return Path.Combine(directory, $"{fileName}.devices.json");
    }

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);
}

public sealed record DeviceEnrollmentResult(
    bool IsAccepted,
    DeviceRecord? Device,
    string? ClientSecret,
    string? Failure)
{
    public static DeviceEnrollmentResult Enrolled(
        DeviceRecord device,
        string clientSecret) =>
        new(true, device, clientSecret, null);

    public static DeviceEnrollmentResult Rejected(string failure) =>
        new(false, null, null, failure);
}
