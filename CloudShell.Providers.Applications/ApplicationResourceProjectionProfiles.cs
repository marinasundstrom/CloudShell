using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class ApplicationResourceProjectionProfiles
{
    public static ApplicationResourceProjection CreateInfrastructureProjection(
        ApplicationResourceDefinition application)
    {
        if (string.Equals(
                application.ResourceType,
                ApplicationResourceTypes.AspNetCoreProject,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ApplicationResourceProjection(
                _ => true,
                _ => "ASP.NET Core project",
                current => ApplicationResourceProjectionSupport.FirstNonEmpty(
                    Path.GetFileName(current.ProjectPath),
                    "project") ?? "project",
                _ => ResourceWorkloadKind.AspNetCoreProject.ToString(),
                _ => ResourceClass.Project);
        }

        if (string.Equals(
                application.ResourceType,
                ApplicationResourceTypes.SqlServer,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ApplicationResourceProjection(
                _ => true,
                _ => "SQL Server",
                ApplicationResourceProjectionSupport.GetContainerVersion,
                ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
                _ => ResourceClass.Service);
        }

        if (ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            return new ApplicationResourceProjection(
                _ => true,
                _ => "Container app",
                ApplicationResourceProjectionSupport.GetContainerVersion,
                ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
                _ => ResourceClass.Container);
        }

        return new ApplicationResourceProjection(
            _ => true,
            current => ApplicationResourceProjectionSupport.IsContainerBacked(current)
                ? "Container app"
                : "Executable application",
            current => ApplicationResourceProjectionSupport.IsContainerBacked(current)
                ? ApplicationResourceProjectionSupport.GetContainerVersion(current)
                : Path.GetFileName(current.ExecutablePath),
            current => ApplicationResourceProjectionSupport.IsContainerBacked(current)
                ? ApplicationResourceProjectionSupport.GetContainerWorkloadKind(current)
                : ResourceWorkloadKind.LocalExecutable.ToString(),
            current => ApplicationResourceProjectionSupport.IsContainerBacked(current)
                ? ResourceClass.Container
                : ResourceClass.Executable);
    }
}
