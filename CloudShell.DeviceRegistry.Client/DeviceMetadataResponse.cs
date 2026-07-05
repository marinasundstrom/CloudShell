using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.DeviceRegistry.Client;

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
    string? Presence = null,
    string? EnrollmentProfileName = null,
    string? EnrollmentProfileKind = null);
