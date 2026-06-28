using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager.UI;

internal static class ReferenceProviderResourceUiTypes
{
    public static bool IsContainerApplication(string? resourceType) =>
        string.Equals(
            resourceType,
            ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);
}
