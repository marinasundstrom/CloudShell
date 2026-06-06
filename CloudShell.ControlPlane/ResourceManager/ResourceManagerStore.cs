using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceManagerStore(
    IEnumerable<IResourceProvider> providers,
    IResourceGroupStore resourceGroups,
    IResourceRegistrationStore registrations) : IResourceManagerStore
{
    public IReadOnlyList<IResourceProvider> Providers { get; } = providers
        .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<CloudResource> GetAvailableResources() => Providers
        .SelectMany(provider => provider.GetResources())
        .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<CloudResource> GetResources()
    {
        var available = GetAvailableResources();
        var registrationsById = registrations.GetRegistrations()
            .ToDictionary(
                registration => registration.ResourceId,
                StringComparer.OrdinalIgnoreCase);
        var registeredIds = registrationsById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return available
            .Where(resource => IsRegisteredOrDescendant(resource, available, registeredIds))
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

    public CloudResource? GetResource(string id) =>
        GetResources().FirstOrDefault(resource => string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<CloudResource> GetChildren(string resourceId) =>
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
        CloudResource resource,
        IReadOnlyList<CloudResource> available,
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

    private static CloudResource ApplyRegistrationMetadata(
        CloudResource resource,
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
}
