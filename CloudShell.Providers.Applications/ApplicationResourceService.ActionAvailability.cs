using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    public bool CanEvaluateAction(Resource resource, ResourceAction action) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null &&
        (action.Kind is ResourceActionKind.Start or ResourceActionKind.Restart ||
         string.Equals(action.Id, ApplicationResourceActionIds.ReconcileSqlServerAccess, StringComparison.OrdinalIgnoreCase));

    public async Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(action.Id, ApplicationResourceActionIds.ReconcileSqlServerAccess, StringComparison.OrdinalIgnoreCase))
        {
            var sqlServer = store.GetApplication(context.Resource.Id);
            if (sqlServer is null ||
                !string.Equals(sqlServer.ResourceType, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase))
            {
                return "Only SQL Server resources can reconcile database access.";
            }

            if (!IsRunning(sqlServer.Id))
            {
                return $"SQL Server resource '{FormatApplicationResourceName(sqlServer)}' must be running before database access can be reconciled.";
            }

            return sqlServer.SqlDatabases.Count == 0
                ? $"SQL Server resource '{FormatApplicationResourceName(sqlServer)}' has no declared databases to reconcile access for."
                : null;
        }

        if (action.Kind is not (ResourceActionKind.Start or ResourceActionKind.Restart))
        {
            return null;
        }

        var application = store.GetApplication(context.Resource.Id);
        if (application is null)
        {
            return null;
        }

        var referenceReason = GetReferenceUnavailableReason(application, context);
        if (!string.IsNullOrWhiteSpace(referenceReason))
        {
            return referenceReason;
        }

        var settingResolutionReason = await GetSettingResolutionUnavailableReasonAsync(
            application,
            context.ResourceGroupId,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(settingResolutionReason))
        {
            return settingResolutionReason;
        }

        var localProcessReason = GetLocalProcessUnavailableReason(application);
        if (!string.IsNullOrWhiteSpace(localProcessReason))
        {
            return localProcessReason;
        }

        var projectReason = GetProjectUnavailableReason(application);
        if (!string.IsNullOrWhiteSpace(projectReason))
        {
            return projectReason;
        }

        var containerHost = await TryResolveContainerHostForAvailabilityAsync(
            application,
            context.ResourceManager,
            context.PreferredContainerHostId,
            cancellationToken);
        var containerHostReason = await GetContainerHostUnavailableReasonAsync(
            application,
            context.ResourceManager,
            context.PreferredContainerHostId,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(containerHostReason))
        {
            return containerHostReason;
        }

        if (containerHost is not null)
        {
            var registryCredentialReason = GetRegistryCredentialUnavailableReason(application);
            if (!string.IsNullOrWhiteSpace(registryCredentialReason))
            {
                return registryCredentialReason;
            }
        }

        var volumeReason = GetVolumeMountUnavailableReason(
            application.VolumeMounts,
            context.ResourceManager,
            environment.ContentRootPath,
            containerHost);
        if (!string.IsNullOrWhiteSpace(volumeReason))
        {
            return volumeReason;
        }

        return GetEndpointUnavailableReason(application, action.Kind);
    }

    private string? GetProjectUnavailableReason(ApplicationResourceDefinition application)
    {
        if (!IsProjectBacked(application))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(application.ProjectPath))
        {
            return $"Project-backed application resource '{FormatApplicationResourceName(application)}' does not declare a project path.";
        }

        var projectPath = application.ProjectPath.Trim();
        var resolvedPath = ResolveProjectPath(application);
        return File.Exists(resolvedPath) || Directory.Exists(resolvedPath)
            ? null
            : $"Project-backed application resource '{FormatApplicationResourceName(application)}' cannot start because project path '{projectPath}' was not found at '{resolvedPath}'.";
    }

    private string? GetLocalProcessUnavailableReason(ApplicationResourceDefinition application)
    {
        if (IsContainerBacked(application))
        {
            return null;
        }

        var workingDirectory = ResolveConfiguredWorkingDirectory(application);
        if (!Directory.Exists(workingDirectory))
        {
            return $"Application resource '{FormatApplicationResourceName(application)}' cannot start because working directory '{workingDirectory}' was not found.";
        }

        if (IsProjectBacked(application))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(application.ExecutablePath))
        {
            return $"Executable application resource '{FormatApplicationResourceName(application)}' does not declare an executable path.";
        }

        var executablePath = application.ExecutablePath.Trim();
        if (!IsExplicitExecutablePath(executablePath))
        {
            return null;
        }

        var resolvedPath = ResolveConfiguredExecutablePath(application, workingDirectory);
        return File.Exists(resolvedPath)
            ? null
            : $"Executable application resource '{FormatApplicationResourceName(application)}' cannot start because executable path '{executablePath}' was not found at '{resolvedPath}'.";
    }

    private static string? GetRegistryCredentialUnavailableReason(ApplicationResourceDefinition application)
    {
        if (!IsContainerBacked(application))
        {
            return null;
        }

        var credentials = ContainerRegistryCredentials.Normalize(application.ContainerRegistryCredentials);
        if (credentials is null)
        {
            return null;
        }

        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable(credentials.NormalizedPasswordEnvironmentVariable))
            ? $"Container app resource '{FormatApplicationResourceName(application)}' cannot access registry '{GetImageRegistryAddress(GetEffectiveContainerRegistry(application))}' because credential environment variable '{credentials.NormalizedPasswordEnvironmentVariable}' is not configured."
            : null;
    }
}
