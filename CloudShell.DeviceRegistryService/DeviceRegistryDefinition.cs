using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.DeviceRegistryService;

public sealed record DeviceRegistryDefinition
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<DeviceRegistryTrustedCertificate> TrustedCertificates { get; init; } = [];

    public DeviceRegistryEnrollmentPolicy EnrollmentPolicy { get; init; } = new();

    public IReadOnlyList<DeviceEnrollmentProfile> EnrollmentProfiles { get; init; } = [];

    public IReadOnlyList<ResourcePermissionGrant> PermissionGrants { get; init; } = [];

    public int? HeartbeatStaleAfterSeconds { get; init; }
}

public sealed record DeviceRegistryTrustedCertificate(
    string VaultResourceId,
    string Name,
    string? Version = null);

public sealed record DeviceRegistryEnrollmentPolicy
{
    public IReadOnlyList<string> Subjects { get; init; } = [];

    public IReadOnlyList<string> SubjectPrefixes { get; init; } = [];

    public IReadOnlyList<DeviceRegistryRequiredClaim> RequiredClaims { get; init; } = [];
}

public sealed record DeviceRegistryRequiredClaim(
    string Name,
    string Value);

public sealed record DeviceEnrollmentProfile
{
    public string Name { get; init; } = "default";

    public string Kind { get; init; } = DeviceEnrollmentProfileKinds.Group;

    public DeviceRegistryEnrollmentPolicy Policy { get; init; } = new();

    public IReadOnlyList<DeviceEnrollmentPermissionGrant> PermissionGrants { get; init; } = [];
}

public static class DeviceEnrollmentProfileKinds
{
    public const string Individual = "individual";
    public const string Group = "group";
}

public sealed record DeviceEnrollmentPermissionGrant(
    string TargetResourceId,
    string Permission);

public sealed record DeviceRecord(
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
    public string Status { get; init; } = DeviceRecordStatuses.Active;

    public DateTimeOffset? LastSeenAt { get; init; }

    public string? LastSeenSource { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public string? RevokedReason { get; init; }
}

public static class DeviceRecordStatuses
{
    public const string Active = "active";
    public const string Revoked = "revoked";
}

public static class DevicePresenceStatuses
{
    public const string Online = "online";
    public const string Stale = "stale";
    public const string Unknown = "unknown";
    public const string Revoked = "revoked";
}

public sealed record DeviceEnrollmentRequest(
    string Subject,
    IReadOnlyDictionary<string, string>? Claims = null,
    IReadOnlyDictionary<string, string>? Properties = null);

public sealed record DeviceHeartbeatRequest(
    IReadOnlyDictionary<string, string>? Properties = null,
    string? Source = null);

public sealed record DeviceRevokeRequest(
    string? Reason = null);

public sealed record DeviceEnrollmentResponse(
    string DeviceId,
    string RegistryId,
    string Subject,
    string IdentityCategory,
    ResourcePrincipalReference Principal,
    string IdentityProviderId,
    string IdentityResourceId,
    string IdentityName,
    string ClientId,
    string ClientSecret,
    string TokenEndpoint,
    DateTimeOffset EnrolledAt,
    string Status,
    DateTimeOffset? LastSeenAt,
    string? LastSeenSource,
    DateTimeOffset? RevokedAt,
    string? RevokedReason,
    IReadOnlyDictionary<string, string> Claims,
    IReadOnlyDictionary<string, string> Properties,
    string? Presence = null);

public sealed record DeviceMetadataResponse(
    string DeviceId,
    string Subject,
    string IdentityCategory,
    ResourcePrincipalReference Principal,
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
    string? Presence = null);
