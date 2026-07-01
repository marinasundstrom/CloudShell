namespace CoreShell;

public sealed record CoreShellClaimRequirement(string Type, string? Value = null);

public sealed record CoreShellAuthorizationRequirements
{
    public static readonly CoreShellAuthorizationRequirements None = new();

    public CoreShellAuthorizationRequirements(
        IReadOnlyList<string>? anyPermissions = null,
        IReadOnlyList<string>? policies = null,
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<CoreShellClaimRequirement>? claims = null)
    {
        AnyPermissions = Normalize(anyPermissions);
        Policies = Normalize(policies);
        Roles = Normalize(roles);
        Claims = claims?
            .Where(claim => !string.IsNullOrWhiteSpace(claim.Type))
            .Select(claim => new CoreShellClaimRequirement(claim.Type.Trim(), NormalizeClaimValue(claim.Value)))
            .Distinct()
            .ToArray()
            ?? [];
    }

    public IReadOnlyList<string> AnyPermissions { get; init; }

    public IReadOnlyList<string> Policies { get; init; }

    public IReadOnlyList<string> Roles { get; init; }

    public IReadOnlyList<CoreShellClaimRequirement> Claims { get; init; }

    public bool IsEmpty =>
        AnyPermissions.Count == 0 &&
        Policies.Count == 0 &&
        Roles.Count == 0 &&
        Claims.Count == 0;

    public static CoreShellAuthorizationRequirements FromAnyPermissions(
        IReadOnlyList<string>? permissions) =>
        new(anyPermissions: permissions);

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
