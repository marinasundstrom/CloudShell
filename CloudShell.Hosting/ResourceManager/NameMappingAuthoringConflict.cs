using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public sealed record NameMappingAuthoringConflict(
    Resource Resource,
    string HostName,
    ResourceExposureScope Exposure)
{
    public string Message =>
        $"DNS zone already has a name mapping for host '{HostName}' in exposure scope '{Exposure}': {Resource.Name}.";
}

public static class NameMappingAuthoringConflicts
{
    private const string NameMappingResourceType = "cloudshell.nameMapping";

    public static NameMappingAuthoringConflict? FindDuplicate(
        IReadOnlyList<Resource> resources,
        string zoneResourceId,
        string hostName,
        ResourceExposureScope exposure,
        string? currentResourceId = null)
    {
        if (string.IsNullOrWhiteSpace(zoneResourceId) ||
            string.IsNullOrWhiteSpace(hostName))
        {
            return null;
        }

        var normalizedHostName = NormalizeHostName(hostName);
        var conflict = resources.FirstOrDefault(resource =>
            IsNameMappingResource(resource) &&
            string.Equals(resource.ParentResourceId, zoneResourceId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(resource.Id, currentResourceId, StringComparison.OrdinalIgnoreCase) &&
            HasNameMappingConflict(resource, normalizedHostName, exposure));

        return conflict is null
            ? null
            : new NameMappingAuthoringConflict(conflict, hostName.Trim(), exposure);
    }

    private static bool IsNameMappingResource(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, NameMappingResourceType, StringComparison.OrdinalIgnoreCase) ||
        resource.HasCapability(ResourceCapabilityIds.NetworkingNameMapping);

    private static bool HasNameMappingConflict(
        Resource resource,
        string normalizedHostName,
        ResourceExposureScope exposure)
    {
        if (!resource.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.NameMappingHostName,
                out var hostName) ||
            !resource.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.NameMappingExposure,
                out var exposureValue) ||
            !Enum.TryParse<ResourceExposureScope>(exposureValue, ignoreCase: true, out var existingExposure))
        {
            return false;
        }

        return string.Equals(NormalizeHostName(hostName), normalizedHostName, StringComparison.OrdinalIgnoreCase) &&
            existingExposure == exposure;
    }

    private static string NormalizeHostName(string hostName) =>
        hostName.Trim().ToLowerInvariant();
}
