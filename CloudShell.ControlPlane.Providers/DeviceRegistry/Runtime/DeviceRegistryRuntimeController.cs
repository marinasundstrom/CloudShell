using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryRuntimeOptions
{
    public string ServiceProjectPath { get; set; } =
        "CloudShell.DeviceRegistryService/CloudShell.DeviceRegistryService.csproj";

    public string? ServiceWorkingDirectory { get; set; }

    public string DefinitionsDirectory { get; set; } = Path.Combine(
        Path.GetTempPath(),
        "CloudShell.ResourceModel",
        "DeviceRegistry");

    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public string? ServiceAuthenticationIssuer { get; set; }

    public string? ServiceAuthenticationAudience { get; set; }

    public string? ServiceAuthenticationSigningKeyPem { get; set; }

    public string ManagementClientId { get; set; } = "device-registry-admin";

    public string ManagementClientSecret { get; set; } =
        "local-development-device-registry-admin-secret";

    public IList<ResourcePermissionGrant> PermissionGrants { get; } = [];
}

public interface IDeviceRegistryRuntimeController
{
    ResourceWebAppRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public interface IDeviceRegistryRuntimeMonitor
{
    ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default);
}

public sealed class DeviceRegistryProcessRuntimeController(
    DeviceRegistryRuntimeOptions? options = null) :
    IDeviceRegistryRuntimeController,
    IDeviceRegistryRuntimeMonitor,
    IDisposable,
    IAsyncDisposable
{
    private readonly DeviceRegistryRuntimeOptions _options =
        options ?? new DeviceRegistryRuntimeOptions();
    private readonly ResourceWebAppProcessRuntime _runtime = new();

    public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
        _runtime.GetStatus(resource);

    public async ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        await _runtime.GetMonitoringSnapshotAsync(resourceId, cancellationToken);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        await _runtime.ExecuteAsync(
            resource,
            operationId,
            DeviceRegistryResourceTypeProvider.Attributes.Endpoint,
            new ResourceWebAppProcessOptions(
                _options.ServiceProjectPath,
                "CloudShell__DeviceRegistryService__DefinitionsPath",
                "CloudShell__DeviceRegistryService__ResourceId",
                "device-registries.json",
                _options.StartupTimeout)
            {
                ServiceWorkingDirectory = _options.ServiceWorkingDirectory,
                DefinitionsDirectory = _options.DefinitionsDirectory,
                EnvironmentVariables = CreateEnvironmentVariables(_options, resource)
            },
            CreateDefinition,
            "iot.deviceRegistry",
            "Device Registry",
            cancellationToken);

    public async ValueTask DisposeAsync() =>
        await _runtime.DisposeAsync();

    public void Dispose() =>
        _runtime.Dispose();

    private object CreateDefinition(
        Resource resource,
        string? endpoint) =>
        new
        {
            id = resource.EffectiveResourceId,
            name = resource.Name,
            displayName = resource.State.DisplayName,
            endpoint,
            trustedCertificates = resource.Attributes
                .GetObject<ResourceCertificateReference[]>(
                    DeviceRegistryResourceTypeProvider.Attributes.TrustedCertificates) ?? [],
            enrollmentPolicy = new
            {
                subjectPrefixes = resource.Attributes
                    .GetObject<string[]>(
                        DeviceRegistryResourceTypeProvider.Attributes.AllowedSubjectPrefixes) ?? [],
                requiredClaims = resource.Attributes
                    .GetObject<DeviceEnrollmentRequiredClaim[]>(
                        DeviceRegistryResourceTypeProvider.Attributes.RequiredClaims) ?? []
            },
            enrollmentProfiles = GetEnrollmentProfiles(resource),
            permissionGrants = GetLegacyPermissionGrants(resource)
                .ToArray(),
            healthChecks = Array.Empty<object>()
        };

    private IReadOnlyList<DeviceEnrollmentProfile> GetEnrollmentProfiles(Resource resource)
    {
        var profiles = resource.Attributes.GetObject<DeviceEnrollmentProfile[]>(
            DeviceRegistryResourceTypeProvider.Attributes.EnrollmentProfiles);
        if (profiles is { Length: > 0 })
        {
            return profiles;
        }

        var legacyGrants = GetLegacyPermissionGrants(resource)
            .Select(grant => new DeviceEnrollmentPermissionGrant(
                grant.TargetResourceId,
                grant.Permission))
            .ToArray();
        if (legacyGrants.Length == 0)
        {
            return [];
        }

        return
        [
            new()
            {
                Name = "default",
                Kind = DeviceEnrollmentProfileKinds.Group,
                Policy = new()
                {
                    SubjectPrefixes = resource.Attributes.GetObject<string[]>(
                        DeviceRegistryResourceTypeProvider.Attributes.AllowedSubjectPrefixes) ?? [],
                    RequiredClaims = resource.Attributes.GetObject<DeviceEnrollmentRequiredClaim[]>(
                        DeviceRegistryResourceTypeProvider.Attributes.RequiredClaims) ?? []
                },
                PermissionGrants = legacyGrants
            }
        ];
    }

    private IEnumerable<ResourcePermissionGrant> GetLegacyPermissionGrants(Resource resource) =>
        _options.PermissionGrants
                .Where(grant =>
                    grant.Principal.Kind == ResourcePrincipalKind.DeviceIdentity &&
                    string.Equals(
                        grant.Principal.SourceResourceId,
                        resource.EffectiveResourceId,
                        StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, string> CreateEnvironmentVariables(
        DeviceRegistryRuntimeOptions options,
        Resource resource)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authentication__BuiltInAuthority__Enabled"] = "true"
        };

        AddIfNotWhiteSpace(
            variables,
            "Authentication__BuiltInAuthority__Issuer",
            options.ServiceAuthenticationIssuer);
        AddIfNotWhiteSpace(
            variables,
            "Authentication__BuiltInAuthority__Audience",
            options.ServiceAuthenticationAudience);
        AddIfNotWhiteSpace(
            variables,
            "Authentication__BuiltInAuthority__SigningKeyPem",
            options.ServiceAuthenticationSigningKeyPem);

        if (!string.IsNullOrWhiteSpace(options.ManagementClientId) &&
            !string.IsNullOrWhiteSpace(options.ManagementClientSecret))
        {
            var clientPath = $"Authentication__BuiltInAuthority__Clients__{options.ManagementClientId}";
            variables[$"{clientPath}__Secret"] = options.ManagementClientSecret;
            variables[$"{clientPath}__Scopes__0"] = "ControlPlane.Access";
            variables[$"{clientPath}__ResourcePermissions__0__ResourceId"] = resource.EffectiveResourceId;
            variables[$"{clientPath}__ResourcePermissions__0__Permission"] =
                DeviceRegistryResourceOperationPermissions.ManageDevices;
            variables[$"{clientPath}__ResourcePermissions__1__ResourceId"] = resource.EffectiveResourceId;
            variables[$"{clientPath}__ResourcePermissions__1__Permission"] =
                DeviceRegistryResourceOperationPermissions.EnrollDevices;
        }

        return variables;
    }

    private static void AddIfNotWhiteSpace(
        IDictionary<string, string> variables,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            variables[name] = value;
        }
    }
}

public sealed class NoopDeviceRegistryRuntimeController :
    IDeviceRegistryRuntimeController,
    IDeviceRegistryRuntimeMonitor
{
    public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
        ResourceWebAppRuntimeStatus.Unknown;

    public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ResourceProcessMonitoringSnapshot?>(null);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
