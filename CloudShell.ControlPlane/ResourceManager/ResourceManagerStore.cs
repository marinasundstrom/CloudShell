using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceManagerStore(
    IEnumerable<IResourceProvider> providers,
    IResourceGroupStore resourceGroups,
    IResourceRegistrationStore registrations,
    ResourceDeclarationStore declarations,
    ResourceIdentityProviderCatalog identityProviders,
    CloudShellExtensionRegistry extensionRegistry,
    ICloudShellExtensionActivationStore activationStore) : IResourceManagerStore
{
    private readonly IReadOnlyList<IResourceProvider> providers = providers.ToArray();

    public IReadOnlyList<IResourceProvider> Providers => providers
        .Where(IsProviderActive)
        .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<Resource> GetAvailableResources()
    {
        var resourceTypeClasses = GetResourceTypeClasses();
        var diagnostics = new List<ResourceModelDiagnostic>();
        return Providers
            .SelectMany(provider => provider.GetResources())
            .Select(resource => ApplyResourceClass(resource, resourceTypeClasses, "provider projection", diagnostics))
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<Resource> GetResources()
    {
        var resourceTypeClasses = GetResourceTypeClasses();
        var diagnostics = new List<ResourceModelDiagnostic>();
        var available = GetAvailableResources();
        var declarationsById = declarations.GetDeclarations()
            .ToDictionary(
                declaration => declaration.ResourceId,
                StringComparer.OrdinalIgnoreCase);
        var projected = available
            .Select(resource => ApplyDeclarationMetadata(resource, declarationsById, resourceTypeClasses, diagnostics))
            .Select(resource => AddIdentityProviderDiagnostic(resource, diagnostics))
            .ToArray();
        var registrationsById = registrations.GetRegistrations()
            .ToDictionary(
                registration => registration.ResourceId,
                StringComparer.OrdinalIgnoreCase);
        var registeredIds = registrationsById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return projected
            .Where(resource => IsRegisteredOrDescendant(resource, projected, registeredIds))
            .Select(resource => ApplyRegistrationMetadata(resource, registrationsById))
            .ToArray();
    }

    public IReadOnlyList<ResourceGroup> GetResourceGroups()
    {
        var registeredByGroup = registrations.GetRegistrations()
            .Where(registration => !string.IsNullOrWhiteSpace(registration.ResourceGroupId))
            .GroupBy(registration => registration.ResourceGroupId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(registration => registration.ResourceId).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return resourceGroups
            .GetResourceGroups()
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group with
            {
                ResourceIds = group.ResourceIds
                    .Concat(registeredByGroup.GetValueOrDefault(group.Id) ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .ToArray();
    }

    public Resource? GetResource(string id) =>
        GetResources().FirstOrDefault(resource => string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics()
    {
        var resourceTypeClasses = GetResourceTypeClasses();
        var diagnostics = new List<ResourceModelDiagnostic>();
        var available = Providers
            .SelectMany(provider => provider.GetResources())
            .Select(resource => ApplyResourceClass(resource, resourceTypeClasses, "provider projection", diagnostics))
            .ToArray();
        var declarationsById = declarations.GetDeclarations()
            .ToDictionary(
                declaration => declaration.ResourceId,
                StringComparer.OrdinalIgnoreCase);

        foreach (var resource in available)
        {
            var projected = ApplyDeclarationMetadata(resource, declarationsById, resourceTypeClasses, diagnostics);
            _ = AddIdentityProviderDiagnostic(projected, diagnostics);
        }

        return diagnostics.ToArray();
    }

    public ResourceClass? GetResourceTypeClass(string resourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        return GetResourceTypeClasses().GetValueOrDefault(resourceType.Trim());
    }

    public IReadOnlyList<Resource> GetChildren(string resourceId) =>
        GetResources()
            .Where(resource => string.Equals(resource.ParentResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ResourceGroup? GetGroupForResource(string resourceId)
    {
        var resources = GetResources();
        var currentId = resourceId;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (visited.Add(currentId))
        {
            var registration = registrations.GetRegistration(currentId);
            if (!string.IsNullOrWhiteSpace(registration?.ResourceGroupId))
            {
                return GetResourceGroups().FirstOrDefault(group =>
                    string.Equals(group.Id, registration.ResourceGroupId, StringComparison.OrdinalIgnoreCase));
            }

            var directGroup = resourceGroups.GetGroupForResource(currentId);
            if (directGroup is not null)
            {
                return directGroup;
            }

            var resource = resources.FirstOrDefault(item =>
                string.Equals(item.Id, currentId, StringComparison.OrdinalIgnoreCase));

            if (resource?.ParentResourceId is null)
            {
                return null;
            }

            currentId = resource.ParentResourceId;
        }

        return null;
    }

    public bool IsRegistered(string resourceId) =>
        registrations.GetRegistration(resourceId) is not null;

    private static bool IsRegisteredOrDescendant(
        Resource resource,
        IReadOnlyList<Resource> available,
        HashSet<string> registeredIds)
    {
        var current = resource;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (visited.Add(current.Id))
        {
            if (registeredIds.Contains(current.Id))
            {
                return true;
            }

            if (current.ParentResourceId is null)
            {
                return false;
            }

            current = available.FirstOrDefault(item =>
                string.Equals(item.Id, current.ParentResourceId, StringComparison.OrdinalIgnoreCase))!;

            if (current is null)
            {
                return false;
            }
        }

        return false;
    }

    private static Resource ApplyRegistrationMetadata(
        Resource resource,
        IReadOnlyDictionary<string, ResourceRegistration> registrationsById)
    {
        if (!registrationsById.TryGetValue(resource.Id, out var registration) ||
            registration.DependsOn.Count == 0)
        {
            return resource;
        }

        return resource with
        {
            DependsOn = resource.DependsOn
                .Concat(registration.DependsOn)
                .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
                .Select(dependency => dependency.Trim())
                .Where(dependency => !string.Equals(dependency, resource.Id, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private Resource AddIdentityProviderDiagnostic(
        Resource resource,
        List<ResourceModelDiagnostic> diagnostics)
    {
        if (resource.IdentityBinding is not { } identity)
        {
            return resource;
        }

        var resolution = identityProviders.Resolve(identity);
        if (!resolution.IsResolved)
        {
            diagnostics.Add(ResourceModelValidation.CreateResourceIdentityProviderUnresolved(
                resource.Id,
                resource.EffectiveTypeId,
                resource.ResourceClass,
                resolution.Reason ?? "No reason was provided.",
                "identity binding"));
        }

        return resource;
    }

    private static Resource ApplyDeclarationMetadata(
        Resource resource,
        IReadOnlyDictionary<string, ResourceDeclaration> declarationsById,
        IReadOnlyDictionary<string, ResourceClass> resourceTypeClasses,
        List<ResourceModelDiagnostic> diagnostics)
    {
        if (!declarationsById.TryGetValue(resource.Id, out var declaration) ||
            (string.IsNullOrWhiteSpace(declaration.ParentResourceId) &&
             declaration.ResourceClassOverride is null &&
             declaration.ResourceAttributes.Count == 0))
        {
            return resource;
        }

        var resourceClass = ResolveDeclarationResourceClass(resource, declaration, resourceTypeClasses, diagnostics);

        return resource with
        {
            ParentResourceId = string.IsNullOrWhiteSpace(declaration.ParentResourceId)
                ? resource.ParentResourceId
                : declaration.ParentResourceId,
            ResourceClass = resourceClass,
            Attributes = MergeAttributes(resource.ResourceAttributes, declaration.ResourceAttributes)
        };
    }

    private static IReadOnlyDictionary<string, string> MergeAttributes(
        IReadOnlyDictionary<string, string> providerAttributes,
        IReadOnlyDictionary<string, string> declarationAttributes)
    {
        if (declarationAttributes.Count == 0)
        {
            return providerAttributes;
        }

        var merged = new Dictionary<string, string>(providerAttributes, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in declarationAttributes)
        {
            merged[name] = value;
        }

        return merged;
    }

    private IReadOnlyDictionary<string, ResourceClass> GetResourceTypeClasses()
    {
        var resourceTypeClasses = new Dictionary<string, ResourceClass>(StringComparer.OrdinalIgnoreCase)
        {
            [PlatformResourceProvider.NetworkResourceType] = ResourceClass.Network,
            [PlatformResourceProvider.VirtualNetworkResourceType] = ResourceClass.Network,
            [PlatformResourceProvider.ServiceResourceType] = ResourceClass.Service,
            [PlatformResourceProvider.LoadBalancerResourceType] = ResourceClass.Network
        };

        foreach (var resourceType in extensionRegistry
                     .GetActiveExtensions(activationStore)
                     .SelectMany(extension => extension.ResourceTypes))
        {
            resourceTypeClasses[resourceType.Id] = resourceType.ResourceClass;
        }

        return resourceTypeClasses;
    }

    private static Resource ApplyResourceClass(
        Resource resource,
        IReadOnlyDictionary<string, ResourceClass> resourceTypeClasses,
        string source,
        List<ResourceModelDiagnostic> diagnostics)
    {
        if (!resourceTypeClasses.TryGetValue(resource.EffectiveTypeId, out var expectedResourceClass))
        {
            return resource;
        }

        var result = ResourceModelValidation.ValidateResourceClass(
            resource.Id,
            resource.EffectiveTypeId,
            expectedResourceClass,
            resource.ResourceClass,
            source);

        if (result.Succeeded)
        {
            return resource;
        }

        diagnostics.Add(result.Diagnostic!);
        return resource with { ResourceClass = expectedResourceClass };
    }

    private static ResourceClass ResolveDeclarationResourceClass(
        Resource resource,
        ResourceDeclaration declaration,
        IReadOnlyDictionary<string, ResourceClass> resourceTypeClasses,
        List<ResourceModelDiagnostic> diagnostics)
    {
        if (declaration.ResourceClassOverride is null)
        {
            return resource.ResourceClass;
        }

        if (!resourceTypeClasses.TryGetValue(resource.EffectiveTypeId, out var expectedResourceClass))
        {
            return declaration.ResourceClassOverride.Value;
        }

        var result = ResourceModelValidation.ValidateResourceClass(
            resource.Id,
            resource.EffectiveTypeId,
            expectedResourceClass,
            declaration.ResourceClassOverride.Value,
            "declaration metadata");

        if (result.Succeeded)
        {
            return declaration.ResourceClassOverride.Value;
        }

        diagnostics.Add(result.Diagnostic!);
        return expectedResourceClass;
    }

    private bool IsProviderActive(IResourceProvider provider)
    {
        var extensionProviderTypes = extensionRegistry
            .Extensions
            .SelectMany(extension => extension.ResourceProviderTypes)
            .ToArray();
        var providerType = provider.GetType();
        var isExtensionProvider = extensionProviderTypes.Any(type => type.IsAssignableFrom(providerType));
        if (!isExtensionProvider)
        {
            return true;
        }

        return extensionRegistry
            .GetActiveExtensions(activationStore)
            .SelectMany(extension => extension.ResourceProviderTypes)
            .Any(type => type.IsAssignableFrom(providerType));
    }
}
