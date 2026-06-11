using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Authentication;

public sealed class BuiltInResourceIdentityRegistry
{
    public const string ClientSecretSettingName = "clientSecret";
    public const string DefaultScope = "ControlPlane.Access";

    private readonly Dictionary<string, BuiltInAuthorityClientOptions> clients =
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

public sealed class BuiltInResourceIdentityProvisioner(
    BuiltInResourceIdentityRegistry registry) : IResourceIdentityProvisioner
{
    public string ProviderId => "built-in";

    public bool CanProvision(ResourceIdentityProviderDefinition provider) =>
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
}
