using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceTemplateOperations(
    ApplicationProviderOptions options,
    IApplicationResourceDefinitionSource definitions,
    IApplicationResourceRegistrationOperations registrations,
    ApplicationResourceDefinitionRegistrationService definitionRegistrations) : IApplicationResourceTemplateOperations
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool CanExport(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        definitions.GetApplication(resource.Id) is not null;

    public Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        var application = definitions.GetApplication(resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{resource.Id}' is not configured.");

        var configuration = new ApplicationResourceTemplateConfiguration(
            ExecutablePath: application.ExecutablePath,
            Arguments: application.Arguments,
            WorkingDirectory: application.WorkingDirectory,
            Endpoint: application.Endpoint,
            EnvironmentVariables: application.EnvironmentVariables,
            Lifetime: application.Lifetime,
            References: application.References,
            UseServiceDiscovery: application.UseServiceDiscovery,
            AppSettings: application.AppSettings,
            Observability: GetEffectiveObservability(application),
            ContainerImage: application.ContainerImage,
            ContainerRegistry: IsContainerBacked(application) ? GetEffectiveContainerRegistry(application) : null,
            ContainerBuildContext: application.ContainerBuildContext,
            ContainerDockerfile: application.ContainerDockerfile,
            ContainerHostId: application.ContainerHostId,
            Replicas: application.Replicas,
            EndpointPorts: application.EndpointPorts,
            ProjectPath: application.ProjectPath,
            ProjectArguments: application.ProjectArguments,
            AspNetCoreHotReload: application.AspNetCoreHotReload,
            ProjectContainerBuild: application.ProjectContainerBuild,
            UseLaunchSettingsEndpoints: application.UseLaunchSettingsEndpoints,
            ReplicasEnabled: application.ReplicasEnabled,
            SqlDatabases: application.SqlDatabases);

        return Task.FromResult(new ResourceTemplateDefinition(
            application.Name,
            ApplicationResourceProviderIds.Applications,
            application.ResourceType,
            resource.DependsOn,
            "1.0",
            JsonSerializer.SerializeToElement(configuration, SerializerOptions),
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
            SerializerOptions)
            ?? throw new InvalidOperationException("The application resource template configuration is invalid.");

        var resourceId = string.IsNullOrWhiteSpace(template.ResourceId)
            ? definitionRegistrations.CreateUniqueImportId(template.Name)
            : definitionRegistrations.ValidateAvailableImportId(template.ResourceId);
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

        await registrations.SetupApplicationAsync(
            definition,
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        return new ResourceTemplateImportResult(
            resourceId,
            $"Imported application resource '{template.Name}'.");
    }

    private ResourceObservability GetEffectiveObservability(ApplicationResourceDefinition definition) =>
        definition.Observability ??
        (options.EnableObservabilityByDefault
            ? ResourceObservability.Default
            : ResourceObservability.None);

    private static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        ApplicationResourceProjectionSupport.IsContainerBacked(application);

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        string.IsNullOrWhiteSpace(application.ContainerRegistry)
            ? ContainerRegistryDefaults.Default
            : application.ContainerRegistry.Trim();

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
