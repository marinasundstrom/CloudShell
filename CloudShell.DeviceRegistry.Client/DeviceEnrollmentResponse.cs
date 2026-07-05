using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.DeviceRegistry.Client;

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
    string? Presence = null,
    string? EnrollmentProfileName = null,
    string? EnrollmentProfileKind = null);
