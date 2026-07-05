using CloudShell.ControlPlane.Providers;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers.UI;

internal static class BuiltInProviderResourceUiTypes
{
    public static bool IsApplicationResource(string? resourceType) =>
        IsExecutableApplication(resourceType) ||
        IsAspNetCoreProject(resourceType) ||
        IsJavaScriptApp(resourceType) ||
        IsJavaApp(resourceType) ||
        IsGoApp(resourceType) ||
        IsPythonApp(resourceType) ||
        IsContainerApplication(resourceType) ||
        IsSqlServer(resourceType) ||
        IsRabbitMQ(resourceType);

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

    public static bool IsJavaScriptApp(string? resourceType) =>
        string.Equals(
            resourceType,
            JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsJavaApp(string? resourceType) =>
        string.Equals(
            resourceType,
            JavaAppResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsGoApp(string? resourceType) =>
        string.Equals(
            resourceType,
            GoAppResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsPythonApp(string? resourceType) =>
        string.Equals(
            resourceType,
            PythonAppResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsSqlServer(string? resourceType) =>
        string.Equals(
            resourceType,
            SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public static bool IsRabbitMQ(string? resourceType) =>
        string.Equals(
            resourceType,
            RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
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
