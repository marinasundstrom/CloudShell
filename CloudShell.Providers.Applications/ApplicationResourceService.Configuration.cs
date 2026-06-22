using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    public bool CanConfigureEnvironmentVariables(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public bool CanConfigureAppSettings(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    private async Task<string?> GetSettingResolutionUnavailableReasonAsync(
        ApplicationResourceDefinition application,
        string? resourceGroupId,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await ResolveConfiguredEnvironmentVariablesAsync(
                application,
                resourceGroupId,
                cancellationToken);
            return null;
        }
        catch (ResourceSettingResolutionException exception)
        {
            return exception.Message;
        }
    }

    public IReadOnlyList<EnvironmentVariableAssignment> GetConfiguredEnvironmentVariables(string resourceId) =>
        store.GetApplication(resourceId)?.EnvironmentVariables ?? [];

    public IReadOnlyList<AppSetting> GetConfiguredAppSettings(string resourceId) =>
        store.GetApplication(resourceId)?.AppSettings ?? [];

    public async Task<ResourceProcedureResult> UpdateAppSettingsAsync(
        ResourceProcedureContext context,
        IReadOnlyList<AppSetting> appSettings,
        CancellationToken cancellationToken = default)
    {
        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{context.Resource.Id}' is not configured.");
        var dependencies = ApplicationConfigurationReferences.GetDependencyResourceIds(
                application.DependsOn,
                appSettings,
                application.EnvironmentVariables)
            .ToArray();
        var definition = application with
        {
            AppSettings = appSettings,
            DependsOn = dependencies
        };
        var restartRequired =
            IsRunning(application.Id) &&
            !application.AppSettings.SequenceEqual(appSettings);

        await UpdateApplicationAsync(
            definition,
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        AppendConfigurationEvent(
            application.Id,
            ResourceEventTypes.Events.Configuration.AppSettingsUpdated,
            $"Updated {appSettings.Count} app setting{Pluralize(appSettings.Count)}.");

        return restartRequired
            ? ResourceProcedureResult.CompletedWithRestartRequired(
                "App settings updated.",
                application.Id,
                "The resource is running. Restart it now to apply the app setting changes.")
            : ResourceProcedureResult.Completed("App settings updated.");
    }

    public async Task<ResourceProcedureResult> UpdateEnvironmentVariablesAsync(
        ResourceProcedureContext context,
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables,
        CancellationToken cancellationToken = default)
    {
        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{context.Resource.Id}' is not configured.");
        var dependencies = ApplicationConfigurationReferences.GetDependencyResourceIds(
                application.DependsOn,
                application.AppSettings,
                environmentVariables)
            .ToArray();
        var definition = application with
        {
            EnvironmentVariables = environmentVariables,
            DependsOn = dependencies
        };
        var restartRequired =
            IsRunning(application.Id) &&
            !application.EnvironmentVariables.SequenceEqual(environmentVariables);

        await UpdateApplicationAsync(
            definition,
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        AppendConfigurationEvent(
            application.Id,
            ResourceEventTypes.Events.Configuration.EnvironmentVariablesUpdated,
            $"Updated {environmentVariables.Count} environment variable{Pluralize(environmentVariables.Count)}.");

        return restartRequired
            ? ResourceProcedureResult.CompletedWithRestartRequired(
                "Environment variables updated.",
                application.Id,
                "The resource is running. Restart it now to apply the environment changes.")
            : ResourceProcedureResult.Completed("Environment variables updated.");
    }

    private void AppendConfigurationEvent(
        string resourceId,
        string eventType,
        string message) =>
        resourceEvents?.Append(new ResourceEvent(
            resourceId,
            eventType,
            message,
            DateTimeOffset.UtcNow));
}
