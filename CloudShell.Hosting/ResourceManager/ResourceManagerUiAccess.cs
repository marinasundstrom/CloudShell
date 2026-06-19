using System.Collections.ObjectModel;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceManagerUiAccess
{
    public static IReadOnlyDictionary<string, ResourceGroup> EmptyGroupLookup { get; } =
        new ReadOnlyDictionary<string, ResourceGroup>(
            new Dictionary<string, ResourceGroup>(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyDictionary<string, ResourceGroup> CreateGroupLookup(
        IEnumerable<ResourceGroup> groups) =>
        groups
            .SelectMany(group => group.ResourceIds.Select(resourceId => new { resourceId, group }))
            .GroupBy(entry => entry.resourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().group,
                StringComparer.OrdinalIgnoreCase);

    public static async Task<IReadOnlyDictionary<string, ResourceGroup>> CreateGroupLookupAsync(
        IResourceManager resourceManager,
        IEnumerable<Resource> resources)
    {
        var groupsByResourceId = new Dictionary<string, ResourceGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in resources)
        {
            var group = await resourceManager.GetResourceGroupForResourceAsync(resource.Id);
            if (group is not null)
            {
                groupsByResourceId[resource.Id] = group;
            }
        }

        return groupsByResourceId;
    }

    public static ResourceGroup? GetResourceGroup(
        IReadOnlyDictionary<string, ResourceGroup> groupsByResourceId,
        string resourceId) =>
        groupsByResourceId.GetValueOrDefault(resourceId);

    public static string? GetResourceGroupId(
        IReadOnlyDictionary<string, ResourceGroup> groupsByResourceId,
        string resourceId) =>
        GetResourceGroup(groupsByResourceId, resourceId)?.Id;

    public static bool CanManageResource(
        ICloudShellAuthorizationService authorization,
        Resource resource,
        IReadOnlyDictionary<string, ResourceGroup> groupsByResourceId) =>
        CanManageResource(
            authorization,
            resource,
            GetResourceGroupId(groupsByResourceId, resource.Id));

    public static bool CanManageResource(
        ICloudShellAuthorizationService authorization,
        Resource resource,
        string? resourceGroupId) =>
        GetResourceAccessLevel(authorization, resource, resourceGroupId)
            .AllowsManage();

    public static bool CanReferenceResource(
        ICloudShellAuthorizationService authorization,
        Resource resource,
        IReadOnlyDictionary<string, ResourceGroup> groupsByResourceId) =>
        CanReferenceResource(
            authorization,
            resource,
            GetResourceGroupId(groupsByResourceId, resource.Id));

    public static bool CanReferenceResource(
        ICloudShellAuthorizationService authorization,
        Resource resource,
        string? resourceGroupId) =>
        GetResourceAccessLevel(authorization, resource, resourceGroupId)
            .AllowsReference();

    public static bool CanReadResource(
        ICloudShellAuthorizationService authorization,
        Resource resource,
        IReadOnlyDictionary<string, ResourceGroup> groupsByResourceId) =>
        CanReadResource(
            authorization,
            resource,
            GetResourceGroupId(groupsByResourceId, resource.Id));

    public static bool CanReadResource(
        ICloudShellAuthorizationService authorization,
        Resource resource,
        string? resourceGroupId) =>
        GetResourceAccessLevel(authorization, resource, resourceGroupId)
            .AllowsRead();

    public static ResourceAccessLevel GetResourceAccessLevel(
        ICloudShellAuthorizationService authorization,
        Resource resource,
        IReadOnlyDictionary<string, ResourceGroup> groupsByResourceId) =>
        GetResourceAccessLevel(
            authorization,
            resource,
            GetResourceGroupId(groupsByResourceId, resource.Id));

    public static ResourceAccessLevel GetResourceAccessLevel(
        ICloudShellAuthorizationService authorization,
        Resource resource,
        string? resourceGroupId) =>
        authorization.GetResourceAccessLevel(
            resource.Id,
            resourceGroupId,
            resource.ResourceActions
                .Select(ResourceActionPermissions.GetRequiredPermission)
                .Distinct(StringComparer.OrdinalIgnoreCase));
}
