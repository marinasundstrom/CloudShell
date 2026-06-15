namespace CloudShell.Abstractions.ResourceManager;

public sealed class ResourceIdentityOptions
{
    public const string SectionName = "ResourceIdentity";

    public string? DefaultProviderId { get; set; }

    public List<ResourceIdentityProviderOptions> Providers { get; set; } = [];

    public ResourceIdentityProviderCatalog ToCatalog() =>
        new(
            Providers.Select(provider => provider.ToDefinition()),
            DefaultProviderId);
}

public sealed class ResourceIdentityProviderOptions
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? ProvisioningResourceId { get; set; }

    public ResourceIdentityProviderKind Kind { get; set; } = ResourceIdentityProviderKind.Oidc;

    public Dictionary<string, string> Settings { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public ResourceIdentityProviderDefinition ToDefinition() =>
        new(Id, Name, Kind, Settings, ProvisioningResourceId);
}

public static class ResourceIdentityProvisioningResources
{
    public const string ProviderId = "identity.provisioning";

    public const string ResourceType = "cloudshell.identity-provisioning";
}

public sealed record ResourceIdentityProviderDefinition(
    string Id,
    string Name,
    ResourceIdentityProviderKind Kind,
    IReadOnlyDictionary<string, string>? Settings = null,
    string? ProvisioningResourceId = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ProviderSettings => Settings ?? EmptySettings;

    public string? ProvisioningResourceId { get; init; } =
        string.IsNullOrWhiteSpace(ProvisioningResourceId) ? null : ProvisioningResourceId.Trim();
}

public interface IResourceIdentityProvisioner
{
    string ProviderId { get; }

    bool CanProvision(ResourceIdentityProviderDefinition provider);

    Task<ResourceIdentityProvisioningResult> ProvisionAsync(
        ResourceIdentityProvisioningRequest request,
        CancellationToken cancellationToken = default);
}

public interface IResourceIdentityProvisioningStatusProvider
{
    string ProviderId { get; }

    bool CanGetProvisioningStatus(ResourceIdentityProviderDefinition provider);

    Task<ResourceIdentityProvisioningStatusResult> GetProvisioningStatusAsync(
        ResourceIdentityProvisioningRequest request,
        CancellationToken cancellationToken = default);
}

public interface IResourceIdentityProviderSetupHandler
{
    string ProviderId { get; }

    bool CanSetup(ResourceIdentityProviderDefinition provider);

    Task<ResourceIdentityProviderSetupResult> SetupAsync(
        ResourceIdentityProviderSetupRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceIdentityProviderSetupRequest(
    ResourceIdentityProviderDefinition Provider);

public sealed record ResourceIdentityProviderSetupResult(
    string ProviderId,
    IReadOnlyList<ResourceIdentityProvisioningDiagnostic>? Diagnostics = null)
{
    public IReadOnlyList<ResourceIdentityProvisioningDiagnostic> SetupDiagnostics =>
        Diagnostics ?? [];
}

/// <summary>
/// Provides runtime credential acquisition environment for resource workloads
/// that have a resolved resource identity.
/// </summary>
/// <remarks>
/// Public preview API. Providers can use this hook to expose the standard
/// <c>CLOUDSHELL_IDENTITY_*</c> environment contract without leaking
/// provider-specific credential storage into the projected resource model.
/// </remarks>
public interface IResourceIdentityCredentialEnvironmentProvider
{
    string ProviderId { get; }

    bool CanCreateEnvironment(ResourceIdentityProviderDefinition provider);

    IReadOnlyList<EnvironmentVariableAssignment> CreateEnvironment(
        ResourceIdentityCredentialEnvironmentRequest request);
}

public sealed record ResourceIdentityCredentialEnvironmentRequest(
    ResourceIdentityProviderDefinition Provider,
    ResourceIdentityReference Identity,
    ResourceIdentityBinding Binding,
    string DefaultScope);

public sealed record ResourceIdentityProvisioningRequest(
    ResourceIdentityProviderDefinition Provider,
    IReadOnlyList<ResourceIdentityProvisioningEntry> Identities,
    IReadOnlyList<ResourcePermissionGrant> PermissionGrants);

public sealed record ResourceIdentityProvisioningEntry(
    ResourceIdentityReference Identity,
    ResourceIdentityBinding Binding);

public sealed record ResourceIdentityProvisioningStatus(
    ResourceIdentityReference Identity,
    ResourceIdentityProvisioningState State,
    string? Detail = null,
    DateTimeOffset? ObservedAt = null);

public sealed record ResourceIdentityProvisioningStatusResult(
    string ProviderId,
    IReadOnlyList<ResourceIdentityProvisioningStatus> Statuses,
    IReadOnlyList<ResourceIdentityProvisioningDiagnostic>? Diagnostics = null)
{
    public IReadOnlyList<ResourceIdentityProvisioningDiagnostic> ProvisioningDiagnostics =>
        Diagnostics ?? [];
}

public enum ResourceIdentityProvisioningState
{
    Unknown,
    NotProvisioned,
    Provisioned,
    Failed
}

public sealed record ResourceIdentityProvisioningResult(
    string ProviderId,
    IReadOnlyList<ResourceIdentityProvisioningDiagnostic>? Diagnostics = null)
{
    public IReadOnlyList<ResourceIdentityProvisioningDiagnostic> ProvisioningDiagnostics =>
        Diagnostics ?? [];
}

public sealed record ResourceIdentityProvisioningDiagnostic(
    ResourceIdentityProvisioningDiagnosticSeverity Severity,
    string Message,
    ResourceIdentityReference? Identity = null,
    string? ProviderId = null);

public enum ResourceIdentityProvisioningDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed class ResourceIdentityProviderCatalog
{
    private readonly IReadOnlyDictionary<string, ResourceIdentityProviderDefinition> providersById;

    public ResourceIdentityProviderCatalog(
        IEnumerable<ResourceIdentityProviderDefinition>? providers = null,
        string? defaultProviderId = null)
    {
        var normalizedProviders = new Dictionary<string, ResourceIdentityProviderDefinition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers ?? [])
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(provider.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(provider.Name);

            var normalizedProvider = provider with
            {
                Id = provider.Id.Trim(),
                Name = provider.Name.Trim(),
                ProvisioningResourceId = provider.ProvisioningResourceId
            };
            if (!normalizedProviders.TryAdd(normalizedProvider.Id, normalizedProvider))
            {
                throw new InvalidOperationException(
                    $"Resource identity provider '{normalizedProvider.Id}' is already registered.");
            }
        }

        providersById = normalizedProviders;
        Providers = normalizedProviders.Values
            .OrderBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        DefaultProviderId = ResolveDefaultProviderId(defaultProviderId);
    }

    public IReadOnlyList<ResourceIdentityProviderDefinition> Providers { get; }

    public string? DefaultProviderId { get; }

    public ResourceIdentityProviderDefinition? DefaultProvider =>
        DefaultProviderId is null ? null : GetProvider(DefaultProviderId);

    public ResourceIdentityProviderDefinition? GetProvider(string? providerId) =>
        string.IsNullOrWhiteSpace(providerId)
            ? null
            : providersById.GetValueOrDefault(providerId.Trim());

    public ResourceIdentityProviderCatalog Merge(
        IEnumerable<ResourceIdentityProviderDefinition>? providers,
        string? defaultProviderId = null)
    {
        var merged = Providers.ToDictionary(
            provider => provider.Id,
            StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers ?? [])
        {
            ArgumentNullException.ThrowIfNull(provider);
            merged[provider.Id] = provider;
        }

        return new ResourceIdentityProviderCatalog(
            merged.Values,
            string.IsNullOrWhiteSpace(defaultProviderId)
                ? DefaultProviderId
                : defaultProviderId.Trim());
    }

    public ResourceIdentityProviderResolution Resolve(ResourceIdentityBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.Kind == ResourceIdentityBindingKind.Provider)
        {
            var provider = GetProvider(binding.ProviderId);
            return provider is null
                ? ResourceIdentityProviderResolution.Unresolved(
                    binding,
                    $"Resource identity provider '{binding.ProviderId}' is not registered.")
                : ResourceIdentityProviderResolution.Resolved(binding, provider);
        }

        var defaultProvider = DefaultProvider;
        return defaultProvider is null
            ? ResourceIdentityProviderResolution.Unresolved(
                binding,
                "No default resource identity provider is registered.")
            : ResourceIdentityProviderResolution.Resolved(binding, defaultProvider);
    }

    private string? ResolveDefaultProviderId(string? defaultProviderId)
    {
        if (!string.IsNullOrWhiteSpace(defaultProviderId))
        {
            var normalizedDefaultProviderId = defaultProviderId.Trim();
            if (!providersById.ContainsKey(normalizedDefaultProviderId))
            {
                throw new InvalidOperationException(
                    $"Default resource identity provider '{normalizedDefaultProviderId}' is not registered.");
            }

            return normalizedDefaultProviderId;
        }

        return providersById.Count == 1
            ? providersById.Values.Single().Id
            : null;
    }
}

public sealed record ResourceIdentityProviderResolution(
    ResourceIdentityBinding Binding,
    ResourceIdentityProviderDefinition? Provider,
    string? Reason)
{
    public bool IsResolved => Provider is not null;

    public static ResourceIdentityProviderResolution Resolved(
        ResourceIdentityBinding binding,
        ResourceIdentityProviderDefinition provider) =>
        new(binding, provider, null);

    public static ResourceIdentityProviderResolution Unresolved(
        ResourceIdentityBinding binding,
        string reason) =>
        new(binding, null, reason);
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

public sealed record ResourceIdentityReference(
    string ResourceId,
    string? Name = null)
{
    public string ResourceId { get; init; } = RequireResourceId(ResourceId);

    public static ResourceIdentityReference ForResource(
        string resourceId,
        string? name = null) =>
        new(resourceId, NormalizeOptional(name));

    private static string RequireResourceId(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return resourceId.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ResourceIdentityBinding(
    string? ProviderId,
    string? Subject = null,
    IReadOnlyList<string>? Scopes = null,
    IReadOnlyDictionary<string, string>? Claims = null,
    ResourceIdentityBindingKind Kind = ResourceIdentityBindingKind.Provider,
    string? Name = null)
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
