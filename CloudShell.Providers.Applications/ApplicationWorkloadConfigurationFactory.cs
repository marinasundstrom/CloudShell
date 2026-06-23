using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationWorkloadConfigurationFactory
{
    public ResourceWorkloadConfiguration Create(
        ApplicationResourceDefinition application,
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables,
        ResourceObservability observability)
    {
        if (!string.IsNullOrWhiteSpace(application.ContainerImage))
        {
            return CreateCommon(
                application,
                ResourceWorkloadKind.ContainerImage,
                environmentVariables,
                observability) with
            {
                Image = application.ContainerImage,
                Registry = GetEffectiveContainerRegistry(application),
                ContainerHostId = application.ContainerHostId
            };
        }

        if (!string.IsNullOrWhiteSpace(application.ContainerBuildContext))
        {
            return CreateCommon(
                application,
                ResourceWorkloadKind.ContainerBuild,
                environmentVariables,
                observability) with
            {
                BuildContext = application.ContainerBuildContext,
                Dockerfile = application.ContainerDockerfile,
                ProjectPath = application.ProjectPath,
                ProjectArguments = application.ProjectArguments,
                Registry = GetEffectiveContainerRegistry(application),
                ContainerHostId = application.ContainerHostId
            };
        }

        if (application.ProjectContainerBuild)
        {
            return CreateCommon(
                application,
                ResourceWorkloadKind.ContainerBuild,
                environmentVariables,
                observability) with
            {
                Dockerfile = application.ContainerDockerfile,
                ProjectPath = application.ProjectPath,
                ProjectArguments = application.ProjectArguments,
                Registry = GetEffectiveContainerRegistry(application),
                ContainerHostId = application.ContainerHostId
            };
        }

        if (ApplicationResourceTypes.IsAspNetCoreProject(application.ResourceType))
        {
            return CreateCommon(
                application,
                ResourceWorkloadKind.AspNetCoreProject,
                environmentVariables,
                observability) with
            {
                WorkingDirectory = application.WorkingDirectory,
                ProjectPath = application.ProjectPath,
                ProjectArguments = application.ProjectArguments,
                AspNetCoreHotReload = application.AspNetCoreHotReload
            };
        }

        return CreateCommon(
            application,
            ResourceWorkloadKind.LocalExecutable,
            environmentVariables,
            observability) with
        {
            ExecutablePath = application.ExecutablePath,
            Arguments = application.Arguments,
            WorkingDirectory = application.WorkingDirectory
        };
    }

    private static ResourceWorkloadConfiguration CreateCommon(
        ApplicationResourceDefinition application,
        ResourceWorkloadKind kind,
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables,
        ResourceObservability observability) =>
        new(
            kind,
            application.Name,
            Replicas: Math.Max(1, application.Replicas),
            ReplicasEnabled: IsReplicaModeEnabled(application),
            AppSettings: application.AppSettings,
            EnvironmentVariables: environmentVariables,
            Ports: application.EndpointPorts,
            Lifetime: ToResourceLifetime(application.Lifetime),
            Observability: observability,
            VolumeMounts: application.VolumeMounts);

    private static bool IsReplicaModeEnabled(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        application.ReplicasEnabled;

    private static ResourceLifetime ToResourceLifetime(ApplicationLifetime lifetime) =>
        lifetime switch
        {
            ApplicationLifetime.ControlPlaneScoped => ResourceLifetime.ControlPlaneScoped,
            _ => ResourceLifetime.Detached
        };

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        string.IsNullOrWhiteSpace(application.ContainerRegistry)
            ? ContainerRegistryDefaults.Default
            : application.ContainerRegistry.Trim();
}
