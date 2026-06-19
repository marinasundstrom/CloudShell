using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class ApplicationResourceProjectionSupport
{
    public static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    public static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        !string.IsNullOrWhiteSpace(application.ContainerImage) ||
        application.ProjectContainerBuild ||
        !string.IsNullOrWhiteSpace(application.ContainerBuildContext);

    public static string GetContainerVersion(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType)
            ? FirstNonEmpty(application.ContainerRevision) ?? "unrevisioned"
            : FirstNonEmpty(application.ContainerImage, application.ContainerBuildContext) ?? "container";

    public static string GetContainerWorkloadKind(ApplicationResourceDefinition application)
    {
        if (!string.IsNullOrWhiteSpace(application.ContainerImage))
        {
            return ResourceWorkloadKind.ContainerImage.ToString();
        }

        if (application.ProjectContainerBuild ||
            !string.IsNullOrWhiteSpace(application.ContainerBuildContext))
        {
            return ResourceWorkloadKind.ContainerBuild.ToString();
        }

        return ResourceWorkloadKind.LocalExecutable.ToString();
    }
}
