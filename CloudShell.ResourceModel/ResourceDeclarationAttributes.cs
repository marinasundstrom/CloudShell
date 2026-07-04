using System.Text.Json;

namespace CloudShell.ResourceModel;

public static class ResourceDeclarationAttributeIds
{
    public static readonly ResourceAttributeId IdentityKind = "identity.kind";
    public static readonly ResourceAttributeId IdentityProviderId = "identity.providerId";
    public static readonly ResourceAttributeId IdentitySubject = "identity.subject";
    public static readonly ResourceAttributeId IdentityScopes = "identity.scopes";
    public static readonly ResourceAttributeId IdentityClaims = "identity.claims";
    public static readonly ResourceAttributeId IdentityName = "identity.name";
    public static readonly ResourceAttributeId IdentityProvisionOnStartup = "identity.provisionOnStartup";
    public static readonly ResourceAttributeId AccessGrants = "access.grants";
}

public sealed record ResourceIdentityBindingAttribute(
    string? ProviderId = null,
    string? Subject = null,
    IReadOnlyList<string>? Scopes = null,
    IReadOnlyDictionary<string, string>? Claims = null,
    string Kind = ResourceIdentityBindingAttributeKinds.Provider,
    string? Name = null);

public static class ResourceIdentityBindingAttributeKinds
{
    public const string Provider = "provider";
    public const string Required = "required";
}

public sealed record ResourceAccessGrantAttribute(
    ResourcePrincipalReferenceAttribute Principal,
    string Permission);

public sealed record ResourcePrincipalReferenceAttribute(
    string Kind,
    string Id,
    string? DisplayName = null,
    string? ProviderId = null,
    string? SourceResourceId = null,
    string? SourceIdentityName = null)
{
    public static ResourcePrincipalReferenceAttribute ForResourceIdentity(
        string resourceId,
        string? identityName = null,
        string? displayName = null,
        string? providerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var normalizedResourceId = resourceId.Trim();
        var normalizedIdentityName = NormalizeOptional(identityName);
        return new(
            ResourcePrincipalAttributeKinds.ResourceIdentity,
            normalizedIdentityName is null
                ? normalizedResourceId
                : $"{normalizedResourceId}/identities/{normalizedIdentityName}",
            NormalizeOptional(displayName),
            NormalizeOptional(providerId),
            normalizedResourceId,
            normalizedIdentityName);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ResourcePrincipalAttributeKinds
{
    public const string ResourceIdentity = "resourceIdentity";
    public const string User = "user";
    public const string Group = "group";
    public const string ServiceAccount = "serviceAccount";
    public const string ServicePrincipal = "servicePrincipal";
    public const string ManagedIdentity = "managedIdentity";
    public const string WorkloadIdentity = "workloadIdentity";
    public const string External = "external";
}

public sealed record ResourceDeclarationAttributeSet(
    ResourceIdentityBindingAttribute? Identity = null,
    bool? ProvisionIdentityOnStartup = null,
    IReadOnlyList<ResourceAccessGrantAttribute>? AccessGrants = null)
{
    public IReadOnlyList<ResourceAccessGrantAttribute> AccessGrantsOrEmpty =>
        AccessGrants ?? [];
}

public static class ResourceDeclarationAttributes
{
    public static ResourceDeclarationAttributeSet GetDeclarationAttributes(
        this ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new(
            definition.GetIdentityAttribute(),
            definition.GetProvisionIdentityOnStartupAttribute(),
            definition.GetAccessGrantAttributes());
    }

    public static ResourceDefinition WithDeclarationAttributes(
        this ResourceDefinition definition,
        ResourceIdentityBindingAttribute? identity = null,
        bool? provisionIdentityOnStartup = null,
        IReadOnlyList<ResourceAccessGrantAttribute>? accessGrants = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var attributes = definition.ResourceAttributeValues.ToDictionary(
            attribute => attribute.Key,
            attribute => attribute.Value);

        if (identity is not null)
        {
            SetIdentity(attributes, identity);
        }

        if (provisionIdentityOnStartup is { } provision)
        {
            attributes[ResourceDeclarationAttributeIds.IdentityProvisionOnStartup] =
                ResourceAttributeValue.Boolean(provision);
        }

        if (accessGrants is { Count: > 0 })
        {
            attributes[ResourceDeclarationAttributeIds.AccessGrants] =
                ResourceAttributeValue.FromObject(accessGrants);
        }

        return definition with
        {
            Attributes = attributes.Count == 0
                ? null
                : new ResourceAttributeValueMap(attributes)
        };
    }

    public static ResourceIdentityBindingAttribute? GetIdentityAttribute(
        this ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var attributes = definition.ResourceAttributeValues;
        var providerId = GetString(attributes, ResourceDeclarationAttributeIds.IdentityProviderId);
        var subject = GetString(attributes, ResourceDeclarationAttributeIds.IdentitySubject);
        var scopes = GetStringList(attributes, ResourceDeclarationAttributeIds.IdentityScopes);
        var claims = GetClaims(attributes);
        var kind = GetString(attributes, ResourceDeclarationAttributeIds.IdentityKind) ??
            (string.IsNullOrWhiteSpace(providerId)
                ? null
                : ResourceIdentityBindingAttributeKinds.Provider);
        var name = GetString(attributes, ResourceDeclarationAttributeIds.IdentityName);

        return string.IsNullOrWhiteSpace(providerId) &&
            string.IsNullOrWhiteSpace(subject) &&
            (scopes is null || scopes.Count == 0) &&
            (claims is null || claims.Count == 0) &&
            string.IsNullOrWhiteSpace(kind) &&
            string.IsNullOrWhiteSpace(name)
                ? null
                : new ResourceIdentityBindingAttribute(
                    providerId,
                    subject,
                    scopes,
                    claims,
                    kind ?? ResourceIdentityBindingAttributeKinds.Required,
                    name);
    }

    public static bool? GetProvisionIdentityOnStartupAttribute(
        this ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!definition.ResourceAttributeValues.TryGetValue(
                ResourceDeclarationAttributeIds.IdentityProvisionOnStartup,
                out var value))
        {
            return null;
        }

        return value.Kind switch
        {
            ResourceAttributeValueKind.Boolean => value.BooleanValue,
            ResourceAttributeValueKind.String when bool.TryParse(value.StringValue, out var parsed) => parsed,
            _ => null
        };
    }

    public static IReadOnlyList<ResourceAccessGrantAttribute> GetAccessGrantAttributes(
        this ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.ResourceAttributeValues.TryGetValue(
                ResourceDeclarationAttributeIds.AccessGrants,
                out var value)
            ? value.ToObject<ResourceAccessGrantAttribute[]>(ResourceDefinitionJson.Options) ?? []
            : [];
    }

    private static void SetIdentity(
        Dictionary<ResourceAttributeId, ResourceAttributeValue> attributes,
        ResourceIdentityBindingAttribute identity)
    {
        SetOptional(attributes, ResourceDeclarationAttributeIds.IdentityKind, identity.Kind);
        SetOptional(attributes, ResourceDeclarationAttributeIds.IdentityProviderId, identity.ProviderId);
        SetOptional(attributes, ResourceDeclarationAttributeIds.IdentitySubject, identity.Subject);
        SetOptional(attributes, ResourceDeclarationAttributeIds.IdentityName, identity.Name);

        if (identity.Scopes is { Count: > 0 })
        {
            attributes[ResourceDeclarationAttributeIds.IdentityScopes] =
                ResourceAttributeValue.FromObject(identity.Scopes);
        }

        if (identity.Claims is { Count: > 0 })
        {
            attributes[ResourceDeclarationAttributeIds.IdentityClaims] =
                ResourceAttributeValue.FromObject(identity.Claims);
        }
    }

    private static void SetOptional(
        Dictionary<ResourceAttributeId, ResourceAttributeValue> attributes,
        ResourceAttributeId attributeId,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[attributeId] = ResourceAttributeValue.String(value.Trim());
        }
    }

    private static string? GetString(
        ResourceAttributeValueMap attributes,
        ResourceAttributeId attributeId) =>
        attributes.TryGetValue(attributeId, out var value) &&
        value.TryGetScalarString(out var scalar) &&
        !string.IsNullOrWhiteSpace(scalar)
            ? scalar.Trim()
            : null;

    private static IReadOnlyList<string>? GetStringList(
        ResourceAttributeValueMap attributes,
        ResourceAttributeId attributeId)
    {
        if (!attributes.TryGetValue(attributeId, out var value))
        {
            return null;
        }

        if (value.Kind == ResourceAttributeValueKind.String &&
            !string.IsNullOrWhiteSpace(value.StringValue))
        {
            return [value.StringValue.Trim()];
        }

        return value.ToObject<string[]>(ResourceDefinitionJson.Options)?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string>? GetClaims(
        ResourceAttributeValueMap attributes)
    {
        var claims = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (attributes.TryGetValue(ResourceDeclarationAttributeIds.IdentityClaims, out var claimsValue) &&
            claimsValue.ToObject<Dictionary<string, string>>(ResourceDefinitionJson.Options) is { } objectClaims)
        {
            foreach (var (name, value) in objectClaims)
            {
                AddClaim(claims, name, value);
            }
        }

        const string prefix = "identity.claims.";
        foreach (var (attributeId, value) in attributes)
        {
            var name = attributeId.ToString();
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !value.TryGetScalarString(out var claimValue))
            {
                continue;
            }

            AddClaim(claims, name[prefix.Length..], claimValue);
        }

        return claims.Count == 0 ? null : claims;
    }

    private static void AddClaim(
        Dictionary<string, string> claims,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(name) &&
            !string.IsNullOrWhiteSpace(value))
        {
            claims[name.Trim()] = value.Trim();
        }
    }
}
