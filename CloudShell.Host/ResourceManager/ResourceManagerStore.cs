using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Host.ResourceManager;

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
        var registeredIds = registrations.GetRegistrations()
            .Select(registration => registration.ResourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return available
            .Where(resource => IsRegisteredOrDescendant(resource, available, registeredIds))
            .ToArray();
    }

    public IReadOnlyList<ResourceGroup> GetResourceGroups() => resourceGroups
        .GetResourceGroups()
        .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

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
}
