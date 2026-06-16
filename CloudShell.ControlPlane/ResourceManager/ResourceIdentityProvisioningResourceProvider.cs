using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceIdentityProvisioningResourceProvider(
    ResourceDeclarationStore declarations) : IResourceProvider
{
    public string Id => ResourceIdentityProvisioningResources.ProviderId;

    public string DisplayName => "Identity Provisioning";

    public IReadOnlyList<Resource> GetResources() =>
        declarations.GetDeclarations()
            .Where(declaration => string.Equals(
                declaration.ProviderId,
                Id,
                StringComparison.OrdinalIgnoreCase))
            .Select(CreateResource)
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private Resource CreateResource(ResourceDeclaration declaration)
    {
        var attributes = new Dictionary<string, string>(
            declaration.ResourceAttributes,
            StringComparer.OrdinalIgnoreCase);
        attributes.TryAdd(ResourceAttributeNames.InfrastructureKind, "identity-provisioning");

        return new Resource(
            declaration.ResourceId,
            GetResourceName(declaration.ResourceId),
            "Identity Provisioning",
            DisplayName,
            "local",
            null,
            [],
            "n/a",
            declaration.DeclaredAt,
            declaration.DependsOn,
            ParentResourceId: declaration.ParentResourceId,
            TypeId: ResourceIdentityProvisioningResources.ResourceType,
            ResourceClass: ResourceClass.Infrastructure,
            Attributes: attributes,
            Source: ResourceSource.User,
            DisplayName: GetDisplayName(declaration));
    }

    private static string GetResourceName(string resourceId) =>
        ResourceId.TryParse(resourceId, out var id) && !string.IsNullOrWhiteSpace(id.Name)
            ? id.Name
            : resourceId;

    private static string GetDisplayName(ResourceDeclaration declaration)
    {
        if (declaration.ResourceAttributes.TryGetValue("identity.provider", out var provider) &&
            !string.IsNullOrWhiteSpace(provider))
        {
            return $"{provider.Trim()} Identity Provisioning";
        }

        var name = declaration.ResourceId.Contains(':', StringComparison.Ordinal)
            ? declaration.ResourceId[(declaration.ResourceId.IndexOf(':', StringComparison.Ordinal) + 1)..]
            : declaration.ResourceId;
        return string.Join(
            " ",
            name.Split(['-', '_', '.', ':'], StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => string.Concat(segment[..1].ToUpperInvariant(), segment[1..])));
    }
}
