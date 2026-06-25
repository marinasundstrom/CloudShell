namespace CloudShell.ResourceDefinitions;

public static class ResourceReferenceRelationships
{
    public static readonly ResourceReferenceRelationship DependsOn = "dependsOn";
    public static readonly ResourceReferenceRelationship BelongsTo = "belongsTo";
}

public static class ResourceReferenceAddressingModes
{
    public static readonly ResourceReferenceAddressingMode ResourceId = "resourceId";
    public static readonly ResourceReferenceAddressingMode ProjectedResource = "projectedResource";
    public static readonly ResourceReferenceAddressingMode ProviderNative = "providerNative";
}

public sealed record ResourceReference(
    string Value,
    ResourceReferenceRelationship Relationship,
    ResourceReferenceAddressingMode AddressingMode,
    ResourceTypeId? TypeId = null,
    string? ProviderId = null)
{
    public static ResourceReference ResourceId(
        string resourceId,
        ResourceReferenceRelationship relationship,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return new(
            resourceId.Trim(),
            relationship,
            ResourceReferenceAddressingModes.ResourceId,
            typeId,
            providerId);
    }

    public static ResourceReference DependsOnResourceId(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null) =>
        ResourceId(
            resourceId,
            ResourceReferenceRelationships.DependsOn,
            typeId,
            providerId);

    public static ResourceReference BelongsToResourceId(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null) =>
        ResourceId(
            resourceId,
            ResourceReferenceRelationships.BelongsTo,
            typeId,
            providerId);

    public bool TryGetResourceId(out string resourceId)
    {
        if (AddressingMode == ResourceReferenceAddressingModes.ResourceId &&
            !string.IsNullOrWhiteSpace(Value))
        {
            resourceId = Value.Trim();
            return true;
        }

        resourceId = string.Empty;
        return false;
    }

    public bool TryGetDependsOnResourceId(out string resourceId)
    {
        if (Relationship == ResourceReferenceRelationships.DependsOn &&
            TryGetResourceId(out resourceId))
        {
            return true;
        }

        resourceId = string.Empty;
        return false;
    }
}
