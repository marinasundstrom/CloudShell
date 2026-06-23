using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed class ProjectBackedApplicationResourceDefinitionNormalizationRule :
    IApplicationResourceDefinitionNormalizationRule
{
    public bool AppliesTo(ApplicationResourceDefinition definition) => true;

    public ApplicationResourceDefinition Normalize(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context)
    {
        if (ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType))
        {
            return definition;
        }

        if (!definition.ProjectContainerBuild)
        {
            return definition with
            {
                ProjectPath = null,
                ProjectArguments = null,
                ProjectContainerBuild = false,
                UseLaunchSettingsEndpoints = false
            };
        }

        return definition with
        {
            ExecutablePath = string.Empty,
            Arguments = null,
            ProjectPath = NormalizeNullable(definition.ProjectPath),
            ProjectArguments = NormalizeNullable(definition.ProjectArguments),
            UseLaunchSettingsEndpoints = false,
            ProjectContainerBuild = string.IsNullOrWhiteSpace(definition.ContainerImage) &&
                definition.ProjectContainerBuild
        };
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ContainerBackedApplicationResourceDefinitionNormalizationRule :
    IApplicationResourceDefinitionNormalizationRule
{
    public bool AppliesTo(ApplicationResourceDefinition definition) => true;

    public ApplicationResourceDefinition Normalize(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context)
    {
        if (!IsContainerBacked(definition))
        {
            return definition with
            {
                ContainerRegistry = null,
                ContainerRegistryCredentials = null
            };
        }

        return definition with
        {
            ContainerRegistry = NormalizeContainerRegistry(definition.ContainerRegistry),
            ContainerRegistryCredentials = ContainerRegistryCredentials.Normalize(definition.ContainerRegistryCredentials)
        };
    }

    private static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        ApplicationResourceProjectionSupport.IsContainerBacked(application);

    private static string NormalizeContainerRegistry(string? registry) =>
        NormalizeNullable(registry) ?? ContainerRegistryDefaults.Default;

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}

public sealed class SqlServerApplicationResourceDefinitionNormalizationRule :
    IApplicationResourceDefinitionNormalizationRule
{
    public bool AppliesTo(ApplicationResourceDefinition definition) => true;

    public ApplicationResourceDefinition Normalize(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context)
    {
        if (!string.Equals(
            definition.ResourceType,
            ApplicationResourceTypes.SqlServer,
            StringComparison.OrdinalIgnoreCase))
        {
            return definition with { SqlDatabases = [] };
        }

        return definition with
        {
            SqlDatabases = definition.SqlDatabases
                .Where(database => !string.IsNullOrWhiteSpace(database.Name))
                .Select(database => new SqlServerDatabaseDefinition(
                    database.Name.Trim(),
                    NormalizeNullable(database.DisplayName),
                    database.EnsureCreated))
                .GroupBy(database => database.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray()
        };
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
