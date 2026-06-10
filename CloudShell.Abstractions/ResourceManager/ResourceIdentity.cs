namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceIdentityProviderDefinition(
    string Id,
    string Name,
    ResourceIdentityProviderKind Kind,
    IReadOnlyDictionary<string, string>? Settings = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ProviderSettings => Settings ?? EmptySettings;
}

public enum ResourceIdentityProviderKind
{
    BuiltIn,
    Managed,
    Oidc,
    Custom
}

public sealed record ResourceIdentityBinding(
    string ProviderId,
    string? Subject = null,
    IReadOnlyList<string>? Scopes = null,
    IReadOnlyDictionary<string, string>? Claims = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyClaims =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> IdentityScopes => Scopes ?? [];

    public IReadOnlyDictionary<string, string> IdentityClaims => Claims ?? EmptyClaims;
}
