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

public enum ResourceIdentityBindingKind
{
    Provider,
    Required
}

public sealed record ResourceIdentityBinding(
    string? ProviderId,
    string? Subject = null,
    IReadOnlyList<string>? Scopes = null,
    IReadOnlyDictionary<string, string>? Claims = null,
    ResourceIdentityBindingKind Kind = ResourceIdentityBindingKind.Provider)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyClaims =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? ProviderId { get; init; } =
        Kind == ResourceIdentityBindingKind.Provider
            ? RequireProviderId(ProviderId)
            : ProviderId;

    public IReadOnlyList<string> IdentityScopes => Scopes ?? [];

    public IReadOnlyDictionary<string, string> IdentityClaims => Claims ?? EmptyClaims;

    public bool HasResolvedProvider => Kind == ResourceIdentityBindingKind.Provider;

    public static ResourceIdentityBinding RequireIdentity(
        IReadOnlyList<string>? scopes = null,
        IReadOnlyDictionary<string, string>? claims = null) =>
        new(
            null,
            Scopes: scopes,
            Claims: claims,
            Kind: ResourceIdentityBindingKind.Required);

    private static string RequireProviderId(string? providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return providerId;
    }
}
