using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    public bool CanExport(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        var application = store.GetApplication(resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{resource.Id}' is not configured.");

        var configuration = new ApplicationResourceTemplateConfiguration(
            application.ExecutablePath,
            application.Arguments,
            application.WorkingDirectory,
            application.Endpoint,
            application.EnvironmentVariables,
            application.Lifetime,
            application.References,
            application.UseServiceDiscovery,
            application.AppSettings,
            GetEffectiveObservability(application),
            application.ContainerImage,
            IsContainerBacked(application) ? GetEffectiveContainerRegistry(application) : null,
            application.ContainerBuildContext,
            application.ContainerDockerfile,
            application.ContainerHostId,
            application.Replicas,
            application.EndpointPorts,
            application.ProjectPath,
            application.ProjectArguments,
            application.AspNetCoreHotReload,
            ProjectContainerBuild: application.ProjectContainerBuild,
            UseLaunchSettingsEndpoints: application.UseLaunchSettingsEndpoints,
            ReplicasEnabled: application.ReplicasEnabled,
            SqlDatabases: application.SqlDatabases);

        return Task.FromResult(new ResourceTemplateDefinition(
            application.Name,
            Id,
            application.ResourceType,
            resource.DependsOn,
            "1.0",
            JsonSerializer.SerializeToElement(configuration, TemplateSerializerOptions),
            application.Id));
    }

    public bool CanImport(ResourceTemplateDefinition template) =>
        ApplicationResourceProviderIds.IsApplicationProvider(template.ProviderId) &&
        ApplicationResourceTypes.IsApplication(template.ResourceType) &&
        string.Equals(template.ProviderConfigurationVersion, "1.0", StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default)
    {
        if (!CanImport(template))
        {
            throw new InvalidOperationException("The application resource template is not supported.");
        }

        var configuration = template.Configuration.Deserialize<ApplicationResourceTemplateConfiguration>(
            TemplateSerializerOptions)
            ?? throw new InvalidOperationException("The application resource template configuration is invalid.");

        var resourceId = string.IsNullOrWhiteSpace(template.ResourceId)
            ? CreateUniqueImportId(template.Name)
            : ValidateAvailableImportId(template.ResourceId);
        var definition = new ApplicationResourceDefinition(
            resourceId,
            template.Name,
            configuration.ExecutablePath,
            arguments: configuration.Arguments,
            workingDirectory: configuration.WorkingDirectory,
            endpoint: configuration.Endpoint,
            environmentVariables: configuration.EnvironmentVariables,
            appSettings: configuration.AppSettings,
            lifetime: configuration.Lifetime,
            dependsOn: context.DependsOn,
            references: configuration.References,
            useServiceDiscovery: configuration.UseServiceDiscovery,
            containerImage: configuration.ContainerImage,
            containerRegistry: configuration.ContainerRegistry,
            containerBuildContext: configuration.ContainerBuildContext,
            containerDockerfile: configuration.ContainerDockerfile,
            projectContainerBuild: configuration.ProjectContainerBuild,
            containerHostId: configuration.ContainerHostId,
            replicas: configuration.Replicas,
            endpointPorts: configuration.EndpointPorts,
            resourceType: template.ResourceType,
            observability: configuration.Observability,
            projectPath: configuration.ProjectPath,
            projectArguments: configuration.ProjectArguments,
            aspNetCoreHotReload: configuration.AspNetCoreHotReload,
            useLaunchSettingsEndpoints: configuration.UseLaunchSettingsEndpoints,
            replicasEnabled: configuration.ReplicasEnabled,
            sqlDatabases: configuration.SqlDatabases);

        await SetupApplicationAsync(
            definition,
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        return new ResourceTemplateImportResult(
            resourceId,
            $"Imported application resource '{template.Name}'.");
    }

    private sealed record ApplicationResourceTemplateConfiguration(
        string ExecutablePath,
        string? Arguments,
        string? WorkingDirectory,
        string? Endpoint,
        IReadOnlyList<EnvironmentVariableAssignment> EnvironmentVariables,
        ApplicationLifetime Lifetime,
        IReadOnlyList<string>? References = null,
        bool UseServiceDiscovery = false,
        IReadOnlyList<AppSetting>? AppSettings = null,
        ResourceObservability? Observability = null,
        string? ContainerImage = null,
        string? ContainerRegistry = null,
        string? ContainerBuildContext = null,
        string? ContainerDockerfile = null,
        string? ContainerHostId = null,
        int Replicas = 1,
        IReadOnlyList<ServicePort>? EndpointPorts = null,
        string? ProjectPath = null,
        string? ProjectArguments = null,
        bool AspNetCoreHotReload = false,
        bool ProjectContainerBuild = false,
        bool UseLaunchSettingsEndpoints = false,
        bool ReplicasEnabled = false,
        IReadOnlyList<SqlServerDatabaseDefinition>? SqlDatabases = null);
}
