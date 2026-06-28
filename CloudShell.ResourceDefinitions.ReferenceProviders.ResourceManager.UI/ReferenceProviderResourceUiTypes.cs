using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager.UI;

internal static class ReferenceProviderResourceUiTypes
{
    public static bool IsSqlServer(string? resourceType) =>
        string.Equals(
            resourceType,
            SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsSqlDatabase(string? resourceType) =>
        string.Equals(
            resourceType,
            SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsContainerApplication(string? resourceType) =>
        string.Equals(
            resourceType,
            ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);
}
