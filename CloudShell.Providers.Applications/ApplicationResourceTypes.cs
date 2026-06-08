namespace CloudShell.Providers.Applications;

public static class ApplicationResourceTypes
{
    public const string ExecutableApplication = "application.executable";

    public const string AspNetCoreProject = "application.aspnet-core-project";

    public const string ContainerApp = "application.container-app";

    public const string ContainerImage = "application.container-image";

    public const string SqlServer = "application.sql-server";

    public static bool IsApplication(string? resourceType) =>
        string.Equals(resourceType, ExecutableApplication, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceType, AspNetCoreProject, StringComparison.OrdinalIgnoreCase) ||
        IsContainerApp(resourceType) ||
        string.Equals(resourceType, SqlServer, StringComparison.OrdinalIgnoreCase);

    public static bool IsContainerApp(string? resourceType) =>
        string.Equals(resourceType, ContainerApp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceType, ContainerImage, StringComparison.OrdinalIgnoreCase);
}
