namespace CloudShell.UI.Composition;

public sealed record CompositionClaimRequirement(string Type, string? Value = null);

public sealed record CompositionAuthorizationRequirements
{
    public static readonly CompositionAuthorizationRequirements None = new();

    public CompositionAuthorizationRequirements(
        IReadOnlyList<string>? anyPermissions = null,
        IReadOnlyList<string>? policies = null,
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<CompositionClaimRequirement>? claims = null)
    {
        AnyPermissions = Normalize(anyPermissions);
        Policies = Normalize(policies);
        Roles = Normalize(roles);
        Claims = claims?
            .Where(claim => !string.IsNullOrWhiteSpace(claim.Type))
            .Select(claim => new CompositionClaimRequirement(claim.Type.Trim(), NormalizeClaimValue(claim.Value)))
            .Distinct()
            .ToArray()
            ?? [];
    }

    public IReadOnlyList<string> AnyPermissions { get; init; }

    public IReadOnlyList<string> Policies { get; init; }

    public IReadOnlyList<string> Roles { get; init; }

    public IReadOnlyList<CompositionClaimRequirement> Claims { get; init; }

    public bool IsEmpty =>
        AnyPermissions.Count == 0 &&
        Policies.Count == 0 &&
        Roles.Count == 0 &&
        Claims.Count == 0;

    public static CompositionAuthorizationRequirements FromAnyPermissions(
        IReadOnlyList<string>? permissions) =>
        new(anyPermissions: permissions);

    public CompositionAuthorizationRequirements WithAnyPermissions(
        IReadOnlyList<string>? permissions) =>
        new(permissions, Policies, Roles, Claims);

    public CompositionAuthorizationRequirements WithPolicies(
        IReadOnlyList<string>? policies) =>
        new(AnyPermissions, policies, Roles, Claims);

    public CompositionAuthorizationRequirements WithRoles(
        IReadOnlyList<string>? roles) =>
        new(AnyPermissions, Policies, roles, Claims);

    public CompositionAuthorizationRequirements WithClaims(
        IReadOnlyList<CompositionClaimRequirement>? claims) =>
        new(AnyPermissions, Policies, Roles, claims);

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    private static string? NormalizeClaimValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
