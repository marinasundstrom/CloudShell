namespace CloudShell.Providers.Applications;

public static class ApplicationResourceProviderIds
{
    public const string Applications = "applications";
    public const string Executable = "applications.executable";
    public const string AspNetCoreProject = "applications.aspnet-core-project";
    public const string ContainerApplication = "applications.container-app";
    public const string SqlServer = "applications.sql-server";

    public static string ForResourceType(string? resourceType)
    {
        if (string.Equals(resourceType, ApplicationResourceTypes.AspNetCoreProject, StringComparison.OrdinalIgnoreCase))
        {
            return AspNetCoreProject;
        }

        if (string.Equals(resourceType, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            return SqlServer;
        }

        if (ApplicationResourceTypes.IsContainerApp(resourceType))
        {
            return ContainerApplication;
        }

        return Executable;
    }

    public static bool IsApplicationProvider(string? providerId) =>
        string.Equals(providerId, Applications, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(providerId, Executable, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(providerId, AspNetCoreProject, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(providerId, ContainerApplication, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(providerId, SqlServer, StringComparison.OrdinalIgnoreCase);
}
