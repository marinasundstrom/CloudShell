namespace CloudShell.Providers.Applications;

public static class ApplicationResourceTypes
{
    public const string ExecutableApplication = "application.executable";

    public const string AspNetCoreProject = "application.aspnet-core-project";

    public const string ContainerImage = "application.container-image";

    public const string SqlServer = "application.sql-server";

    public static bool IsApplication(string? resourceType) =>
        string.Equals(resourceType, ExecutableApplication, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceType, AspNetCoreProject, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceType, ContainerImage, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceType, SqlServer, StringComparison.OrdinalIgnoreCase);
}
