using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Authentication;

public sealed class BuiltInResourceIdentityRegistry
{
    public const string ClientSecretSettingName = "clientSecret";
    public const string DefaultScope = "ControlPlane.Access";

    private readonly Dictionary<string, BuiltInAuthorityClientOptions> clients =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, BuiltInResourceIdentityRegistration> registrations =
        new(StringComparer.Ordinal);
    private readonly object gate = new();

    public void Register(
        ResourceIdentityProviderDefinition provider,
        ResourceIdentityProvisioningEntry entry,
        IReadOnlyList<ResourcePermissionGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(grants);

        var clientId = CreateClientId(entry.Identity);
        var secret = ResolveClientSecret(provider, clientId);
        var matchingGrants = grants
            .Where(grant => Matches(entry.Identity, grant.Identity))
            .ToArray();
        var client = new BuiltInAuthorityClientOptions
        {
            Secret = secret,
            Scopes = entry.Binding.IdentityScopes.Count == 0
                ? [DefaultScope]
                : entry.Binding.IdentityScopes
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            Permissions = matchingGrants
                .Select(grant => grant.Permission)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Resources = matchingGrants
                .Select(grant => grant.TargetResourceId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ResourcePermissions = matchingGrants
                .GroupBy(
                    grant => $"{grant.TargetResourceId}\0{grant.Permission}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new BuiltInAuthorityResourcePermissionOptions
                {
                    ResourceId = group.First().TargetResourceId,
                    Permission = group.First().Permission
                })
                .ToArray()
        };

        lock (gate)
        {
            clients[clientId] = client;
            registrations[clientId] = new BuiltInResourceIdentityRegistration(
                entry.Identity,
                provider.Id,
                clientId);
        }
    }

    public bool TryGetClient(
        string clientId,
        out BuiltInAuthorityClientOptions client)
    {
        lock (gate)
        {
            return clients.TryGetValue(clientId, out client!);
        }
    }

    public bool Contains(ResourceIdentityReference identity) =>
        TryGetClient(CreateClientId(identity), out _);

    public IReadOnlyList<BuiltInResourceIdentityRegistration> ListRegistrations()
    {
        lock (gate)
        {
            return registrations.Values
                .OrderBy(registration => registration.ClientId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static string CreateClientId(ResourceIdentityReference identity) =>
        string.IsNullOrWhiteSpace(identity.Name)
            ? identity.ResourceId
            : $"{identity.ResourceId}/{identity.Name}";

    private static string ResolveClientSecret(
        ResourceIdentityProviderDefinition provider,
        string clientId) =>
        provider.ProviderSettings.TryGetValue(ClientSecretSettingName, out var configuredSecret) &&
        !string.IsNullOrWhiteSpace(configuredSecret)
            ? configuredSecret
            : $"local-development-{Sanitize(clientId)}-secret";

    private static bool Matches(
        ResourceIdentityReference declaredIdentity,
        ResourceIdentityReference grantIdentity) =>
        string.Equals(
            declaredIdentity.ResourceId,
            grantIdentity.ResourceId,
            StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(grantIdentity.Name) ||
         string.Equals(
             declaredIdentity.Name,
             grantIdentity.Name,
             StringComparison.OrdinalIgnoreCase));

    private static string Sanitize(string value)
    {
        var characters = value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        return new string(characters).Trim('-');
    }
}

public sealed record BuiltInResourceIdentityRegistration(
    ResourceIdentityReference Identity,
    string ProviderId,
    string ClientId);

public sealed class BuiltInResourceIdentityProvisioner(
    BuiltInResourceIdentityRegistry registry) :
    IResourceIdentityProvisioner,
    IResourceIdentityProvisioningStatusProvider,
    IResourceIdentityDirectoryProvider
{
    public string ProviderId => "built-in";

    public bool CanProvision(ResourceIdentityProviderDefinition provider) =>
        provider.Kind == ResourceIdentityProviderKind.BuiltIn;

    public bool CanGetProvisioningStatus(ResourceIdentityProviderDefinition provider) =>
        provider.Kind == ResourceIdentityProviderKind.BuiltIn;

    public bool CanQueryDirectory(ResourceIdentityProviderDefinition provider) =>
        provider.Kind == ResourceIdentityProviderKind.BuiltIn;

    public Task<ResourceIdentityProvisioningResult> ProvisionAsync(
        ResourceIdentityProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var entry in request.Identities)
        {
            registry.Register(request.Provider, entry, request.PermissionGrants);
        }

        return Task.FromResult(new ResourceIdentityProvisioningResult(
            request.Provider.Id,
            request.Identities
                .Select(entry => new ResourceIdentityProvisioningDiagnostic(
                    ResourceIdentityProvisioningDiagnosticSeverity.Information,
                    $"Provisioned built-in resource identity client '{BuiltInResourceIdentityRegistry.CreateClientId(entry.Identity)}'.",
                    entry.Identity,
                    request.Provider.Id))
                .ToArray()));
    }

    public Task<ResourceIdentityProvisioningStatusResult> GetProvisioningStatusAsync(
        ResourceIdentityProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ResourceIdentityProvisioningStatusResult(
            request.Provider.Id,
            request.Identities
                .Select(entry =>
                {
                    var isProvisioned = registry.Contains(entry.Identity);
                    return new ResourceIdentityProvisioningStatus(
                        entry.Identity,
                        isProvisioned
                            ? ResourceIdentityProvisioningState.Provisioned
                            : ResourceIdentityProvisioningState.NotProvisioned,
                        isProvisioned
                            ? "Built-in resource identity client is registered."
                            : "Built-in resource identity client is not registered.",
                        DateTimeOffset.UtcNow);
                })
                .ToArray()));
    }

    public Task<ResourceIdentityDirectoryResult> QueryDirectoryAsync(
        ResourceIdentityDirectoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var principals = registry.ListRegistrations()
            .Where(registration => string.Equals(
                registration.ProviderId,
                request.Provider.Id,
                StringComparison.OrdinalIgnoreCase))
            .Select(registration => new ResourcePrincipal(
                registration.Identity.ToPrincipal(
                    registration.Identity.ResourceId,
                    registration.ProviderId),
                registration.Identity.ResourceId,
                "Built-in resource identity client.",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["clientId"] = registration.ClientId,
                    ["resourceId"] = registration.Identity.ResourceId
                }))
            .Where(principal =>
                request.Query.PrincipalKinds.Count == 0 ||
                request.Query.PrincipalKinds.Contains(principal.Reference.Kind))
            .Where(principal =>
                string.IsNullOrWhiteSpace(request.Query.SearchText) ||
                Contains(principal.DisplayName, request.Query.SearchText) ||
                Contains(principal.Reference.Id, request.Query.SearchText) ||
                Contains(principal.Reference.SourceResourceId, request.Query.SearchText) ||
                Contains(principal.Reference.SourceIdentityName, request.Query.SearchText) ||
                principal.PrincipalAttributes.Any(attribute =>
                    Contains(attribute.Key, request.Query.SearchText) ||
                    Contains(attribute.Value, request.Query.SearchText)))
            .ToArray();

        return Task.FromResult(new ResourceIdentityDirectoryResult(
            request.Provider.Id,
            request.Query.Limit is > 0
                ? principals.Take(request.Query.Limit.Value).ToArray()
                : principals));
    }

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(search, StringComparison.OrdinalIgnoreCase);
}
