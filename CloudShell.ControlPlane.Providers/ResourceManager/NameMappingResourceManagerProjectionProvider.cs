using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class NameMappingResourceManagerProjectionProvider :
    IResourceModelResourceManagerAttributeProvider,
    IResourceModelResourceManagerParentProvider
{
    public IReadOnlyDictionary<string, string>? GetAttributes(ResourceModelResource resource)
    {
        if (!IsNameMapping(resource) ||
            !TryGetTargetResourceId(resource, out var targetResourceId))
        {
            return null;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.NameMappingTargetResourceId] = targetResourceId
        };
    }

    public string? GetParentResourceId(ResourceModelResource resource) =>
        IsNameMapping(resource) && TryGetDnsZoneResourceId(resource, out var zoneResourceId)
            ? zoneResourceId
            : null;

    private static bool IsNameMapping(ResourceModelResource resource) =>
        resource.Type.TypeId == NameMappingResourceTypeProvider.ResourceTypeId;

    private static bool TryGetDnsZoneResourceId(
        ResourceModelResource resource,
        out string resourceId) =>
        TryGetReference(
            resource,
            ResourceReferenceRelationships.BelongsTo,
            DnsZoneResourceTypeProvider.ResourceTypeId,
            out resourceId);

    private static bool TryGetTargetResourceId(
        ResourceModelResource resource,
        out string resourceId)
    {
        foreach (var reference in resource.State.ResourceDependencies)
        {
            if (reference.Relationship == ResourceReferenceRelationships.Reference &&
                reference.TypeId != DnsZoneResourceTypeProvider.ResourceTypeId &&
                reference.TryGetResourceId(out resourceId))
            {
                return true;
            }
        }

        resourceId = string.Empty;
        return false;
    }

    private static bool TryGetReference(
        ResourceModelResource resource,
        ResourceReferenceRelationship relationship,
        ResourceTypeId typeId,
        out string resourceId)
    {
        foreach (var reference in resource.State.ResourceDependencies)
        {
            if (reference.Relationship == relationship &&
                reference.TypeId == typeId &&
                reference.TryGetResourceId(out resourceId))
            {
                return true;
            }
        }

        resourceId = string.Empty;
        return false;
    }
}
