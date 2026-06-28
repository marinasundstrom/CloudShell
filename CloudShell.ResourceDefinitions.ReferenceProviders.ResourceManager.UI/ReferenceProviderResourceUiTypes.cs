using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager.UI;

internal static class ReferenceProviderResourceUiTypes
{
    public static bool IsApplicationResource(string? resourceType) =>
        IsExecutableApplication(resourceType) ||
        IsAspNetCoreProject(resourceType) ||
        IsContainerApplication(resourceType) ||
        IsSqlServer(resourceType);

    public static bool IsExecutableApplication(string? resourceType) =>
        string.Equals(
            resourceType,
            ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsAspNetCoreProject(string? resourceType) =>
        string.Equals(
            resourceType,
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

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

    public static bool IsVolumeResource(ResourceManagerResource resource) =>
        string.Equals(
            resource.EffectiveTypeId,
            CloudShellVolumeResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            resource.EffectiveTypeId,
            LocalVolumeResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);
}
