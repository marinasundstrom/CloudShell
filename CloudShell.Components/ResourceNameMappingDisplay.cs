using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public static class ResourceNameMappingDisplay
{
    private const string NameMappingResourceType = "cloudshell.nameMapping";

    public static bool IsNameMappingResource(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, NameMappingResourceType, StringComparison.OrdinalIgnoreCase) ||
        resource.HasCapability(ResourceCapabilityIds.NetworkingNameMapping);

    public static bool TargetsResource(Resource resource, string resourceId) =>
        resource.ResourceAttributes.TryGetValue(ResourceAttributeNames.NameMappingTargetResourceId, out var targetResourceId) &&
        string.Equals(targetResourceId, resourceId, StringComparison.OrdinalIgnoreCase);

    public static string GetHostName(Resource resource) =>
        GetAttribute(resource, ResourceAttributeNames.NameMappingHostName, resource.Name);

    public static string GetTargetEndpointName(Resource resource) =>
        GetAttribute(resource, ResourceAttributeNames.NameMappingTargetEndpointName, "default");

    public static string GetExposureLabel(Resource resource) =>
        GetAttribute(resource, ResourceAttributeNames.NameMappingExposure, ResourceExposureScope.Public.ToString());

    public static string GetProviderLabel(Resource resource) =>
        GetAttribute(resource, ResourceAttributeNames.DnsProvider, "logical");

    public static string GetProviderResourceId(Resource resource) =>
        GetAttribute(resource, ResourceAttributeNames.NameMappingProviderResourceId, string.Empty);

    public static string GetMaterializationLabel(Resource resource) =>
        GetMaterializationLabel(resource, static value => value);

    public static string GetMaterializationLabel(Resource resource, Func<string, string> localize) =>
        GetAttribute(resource, ResourceAttributeNames.NameMappingMaterializationStatus, "unknown") switch
        {
            "LogicalOnly" => localize("logical only"),
            "ProviderSelected" => localize("provider selected"),
            "Published" => localize("published"),
            "PublishFailed" => localize("publish failed"),
            var value => value
        };

    public static string GetSummary(Resource resource, string targetName) =>
        $"{GetHostName(resource)} -> {targetName}/{GetTargetEndpointName(resource)}";

    private static string GetAttribute(Resource resource, string name, string fallback) =>
        resource.ResourceAttributes.TryGetValue(name, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
}
