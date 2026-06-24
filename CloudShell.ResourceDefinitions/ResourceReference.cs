namespace CloudShell.ResourceDefinitions;

public static class ResourceReferenceRelationships
{
    public static readonly ResourceReferenceRelationship DependsOn = "dependsOn";
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
        ResourceReferenceRelationship? relationship = null,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return new(
            resourceId.Trim(),
            relationship ?? ResourceReferenceRelationships.DependsOn,
            ResourceReferenceAddressingModes.ResourceId,
            typeId,
            providerId);
    }

    public bool TryGetResourceId(out string resourceId)
    {
        if (Relationship == ResourceReferenceRelationships.DependsOn &&
            AddressingMode == ResourceReferenceAddressingModes.ResourceId &&
            !string.IsNullOrWhiteSpace(Value))
        {
            resourceId = Value.Trim();
            return true;
        }

        resourceId = string.Empty;
        return false;
    }
}
