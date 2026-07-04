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
        var profile = ResolveEnrollmentProfile(registry, normalizedSubject, claims, out var policyFailure);
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
            if (IsRevoked(existing))
            {
                return DeviceEnrollmentResult.Rejected("Device identity has been revoked.");
            }

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
                existing?.EnrolledAt ?? timestamp)
            {
                Status = DeviceRecordStatuses.Active,
                LastSeenAt = timestamp,
                LastSeenSource = "enrollment"
            };

            if (existing is null)
            {
                devices.Add(record);
            }
            else
            {
                devices[devices.IndexOf(existing)] = record;
            }

            WriteDevices(devices);
            var principal = CreatePrincipal(record);
            identities.Register(
                _identityProvider,
                new ResourceIdentityProvisioningEntry(identity, binding),
                CreatePermissionGrants(registry, profile, principal),
                principal);

            return DeviceEnrollmentResult.Enrolled(
                record,
                BuiltInResourceIdentityRegistry.ResolveClientSecret(_identityProvider, clientId));
        }
    }

    private void RegisterIdentity(DeviceRecord device)
    {
        if (IsRevoked(device))
        {
            identities.Unregister(device.ClientId);
            return;
        }

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
            CreatePermissionGrants(
                registry,
                registry is null
                    ? null
                    : ResolveEnrollmentProfile(registry, device.Subject, device.Claims, out _),
                CreatePrincipal(device)),
            CreatePrincipal(device));
    }

    public DeviceMutationResult RecordHeartbeat(
        string registryId,
        string deviceId,
        string clientId,
        DeviceHeartbeatRequest request,
        DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var devices = LoadDevices().ToList();
            var device = FindDevice(devices, registryId, deviceId);
            if (device is null)
            {
                return DeviceMutationResult.NotFound("The device was not found.");
            }

            if (!string.Equals(device.ClientId, clientId, StringComparison.Ordinal))
            {
                return DeviceMutationResult.Rejected("The device identity cannot update another device.");
            }

            if (IsRevoked(device))
            {
                identities.Unregister(device.ClientId);
                return DeviceMutationResult.Rejected("Device identity has been revoked.");
            }

            var updated = device with
            {
                Properties = MergeProperties(device.Properties, request.Properties),
                LastSeenAt = timestamp,
                LastSeenSource = string.IsNullOrWhiteSpace(request.Source)
                    ? "heartbeat"
                    : request.Source.Trim(),
                Status = DeviceRecordStatuses.Active
            };
            devices[devices.IndexOf(device)] = updated;
            WriteDevices(devices);

            return DeviceMutationResult.Accepted(updated);
        }
    }

    public DeviceMutationResult RevokeDevice(
        string registryId,
        string deviceId,
        string? reason,
        DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        lock (_gate)
        {
            var devices = LoadDevices().ToList();
            var device = FindDevice(devices, registryId, deviceId);
            if (device is null)
            {
                return DeviceMutationResult.NotFound("The device was not found.");
            }

            var updated = device with
            {
                Status = DeviceRecordStatuses.Revoked,
                RevokedAt = device.RevokedAt ?? timestamp,
                RevokedReason = string.IsNullOrWhiteSpace(reason)
                    ? device.RevokedReason
                    : reason.Trim()
            };
            devices[devices.IndexOf(device)] = updated;
            WriteDevices(devices);
            identities.Unregister(updated.ClientId);

            return DeviceMutationResult.Accepted(updated);
        }
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

    private static DeviceEnrollmentProfile? ResolveEnrollmentProfile(
        DeviceRegistryDefinition registry,
        string subject,
        IReadOnlyDictionary<string, string> claims,
        out string? failure)
    {
        foreach (var profile in GetEnrollmentProfiles(registry))
        {
            var profileFailure = ValidatePolicy(profile.Policy, subject, claims);
            if (profileFailure is null)
            {
                failure = null;
                return profile;
            }
        }

        failure = "Device enrollment did not match an enrollment profile.";
        return null;
    }

    private static IReadOnlyList<DeviceEnrollmentProfile> GetEnrollmentProfiles(
        DeviceRegistryDefinition registry) =>
        registry.EnrollmentProfiles.Count > 0
            ? registry.EnrollmentProfiles
            :
            [
                new()
                {
                    Name = "default",
                    Kind = DeviceEnrollmentProfileKinds.Group,
                    Policy = registry.EnrollmentPolicy,
                    PermissionGrants = registry.PermissionGrants
                        .Select(grant => new DeviceEnrollmentPermissionGrant(
                            grant.TargetResourceId,
                            grant.Permission))
                        .ToArray()
                }
            ];

    private static IReadOnlyList<ResourcePermissionGrant> CreatePermissionGrants(
        DeviceRegistryDefinition? registry,
        DeviceEnrollmentProfile? profile,
        ResourcePrincipalReference principal)
    {
        if (registry is null)
        {
            return [];
        }

        var grants = new List<ResourcePermissionGrant>();
        grants.AddRange(registry.PermissionGrants);
        if (profile is not null)
        {
            grants.AddRange(profile.PermissionGrants.Select(grant =>
                new ResourcePermissionGrant(
                    principal,
                    grant.TargetResourceId,
                    grant.Permission)));
        }

        return grants
            .GroupBy(
                grant => $"{grant.Principal.Kind}\u001f{grant.Principal.Id}\u001f{grant.TargetResourceId}\u001f{grant.Permission}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string? ValidatePolicy(
        DeviceRegistryEnrollmentPolicy policy,
        string subject,
        IReadOnlyDictionary<string, string> claims)
    {
        if ((policy.Subjects.Count > 0 || policy.SubjectPrefixes.Count > 0) &&
            !policy.Subjects.Any(allowed =>
                string.Equals(subject, allowed, StringComparison.OrdinalIgnoreCase)) &&
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

    private static IReadOnlyDictionary<string, string> MergeProperties(
        IReadOnlyDictionary<string, string> existing,
        IReadOnlyDictionary<string, string>? updates)
    {
        var merged = new Dictionary<string, string>(
            existing,
            StringComparer.OrdinalIgnoreCase);
        if (updates is null)
        {
            return merged;
        }

        foreach (var (name, value) in NormalizeProperties(updates))
        {
            merged[name] = value;
        }

        return merged;
    }

    private static DeviceRecord? FindDevice(
        IReadOnlyList<DeviceRecord> devices,
        string registryId,
        string deviceId) =>
        devices.FirstOrDefault(device =>
            string.Equals(device.RegistryId, registryId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase));

    private static bool IsRevoked(DeviceRecord? device) =>
        string.Equals(device?.Status, DeviceRecordStatuses.Revoked, StringComparison.OrdinalIgnoreCase) ||
        device?.RevokedAt is not null;

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

public sealed record DeviceMutationResult(
    bool IsAccepted,
    bool IsNotFound,
    DeviceRecord? Device,
    string? Failure)
{
    public static DeviceMutationResult Accepted(DeviceRecord device) =>
        new(true, false, device, null);

    public static DeviceMutationResult Rejected(string failure) =>
        new(false, false, null, failure);

    public static DeviceMutationResult NotFound(string failure) =>
        new(false, true, null, failure);
}
