using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Persistence;

namespace CloudShell.ControlPlane.Authentication;

public sealed class AuthorizedResourceGroupStore(
    EfCoreResourceStore inner,
    ICloudShellAuthorizationService authorization) : IResourceGroupStore
{
    public IReadOnlyList<ResourceGroup> GetResourceGroups() =>
        inner.GetResourceGroups()
            .Where(group => authorization.CanAccessResourceGroup(
                group.Id,
                CloudShellPermissions.ResourceGroups.Read))
            .ToArray();

    public ResourceGroup? GetGroupForResource(string resourceId)
    {
        var group = inner.GetGroupForResource(resourceId);
        return group is not null &&
               authorization.CanAccessResourceGroup(
                   group.Id,
                   CloudShellPermissions.ResourceGroups.Read)
            ? group
            : null;
    }

    public Task<ResourceGroup> CreateAsync(
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.CanAccessResourceGroup(
                "__new_resource_group",
                CloudShellPermissions.ResourceGroups.Create))
        {
            throw new UnauthorizedAccessException(
                $"The '{CloudShellPermissions.ResourceGroups.Create}' permission " +
                "and wildcard resource-group scope are required.");
        }

        return inner.CreateAsync(name, description, cancellationToken);
    }
}
