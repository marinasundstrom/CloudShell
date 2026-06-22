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
        var isAspNetCoreProject = IsAspNetCoreProject(definition);
        if (!isAspNetCoreProject &&
            !definition.ProjectContainerBuild)
        {
            return definition with
            {
                ProjectPath = null,
                ProjectArguments = null,
                ProjectContainerBuild = false,
                UseLaunchSettingsEndpoints = false
            };
        }

        var legacyProjectPath = isAspNetCoreProject
            ? ApplicationProcessDefinitions.TryExtractProjectPathFromDotNetArguments(definition.Arguments)
            : null;
        var projectPath = NormalizeNullable(definition.ProjectPath) ?? legacyProjectPath;

        return definition with
        {
            ExecutablePath = string.Empty,
            Arguments = null,
            ProjectPath = projectPath,
            ProjectArguments = NormalizeNullable(definition.ProjectArguments) ??
                ApplicationProcessDefinitions.TryExtractApplicationArgumentsFromDotNetArguments(definition.Arguments),
            AspNetCoreHotReload = ApplicationProcessDefinitions.ResolveAspNetCoreHotReload(definition),
            UseLaunchSettingsEndpoints = isAspNetCoreProject && definition.UseLaunchSettingsEndpoints,
            ProjectContainerBuild = string.IsNullOrWhiteSpace(definition.ContainerImage) &&
                definition.ProjectContainerBuild
        };
    }

    private static bool IsAspNetCoreProject(ApplicationResourceDefinition definition) =>
        ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType);

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AspNetCoreProjectEndpointNormalizationRule :
    IApplicationResourceDefinitionNormalizationRule
{
    public bool AppliesTo(ApplicationResourceDefinition definition) =>
        ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType);

    public ApplicationResourceDefinition Normalize(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context) =>
        Resolve(definition, context);

    public ApplicationResourceDefinition Resolve(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context)
    {
        if (definition.EndpointPorts.Count > 0)
        {
            return definition;
        }

        var endpointPorts = definition.UseLaunchSettingsEndpoints
            ? context.TryReadLaunchSettingsEndpointPorts(definition.ProjectPath)
            : [];
        return endpointPorts.Count == 0
            ? definition with
            {
                EndpointPorts = ApplicationResourceDefinitionNormalizationContext
                    .CreateAspNetCoreProjectEndpointPorts(definition.Endpoint)
            }
            : definition with { EndpointPorts = endpointPorts };
    }
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
                ContainerRegistryCredentials = null,
                ContainerRevision = null,
                ContainerRevisions = [],
                ReplicasEnabled = false
            };
        }

        var replicasEnabled = definition.ReplicasEnabled || definition.Replicas > 1;
        var containerRevision = NormalizeNullable(definition.ContainerRevision) ?? CreateContainerRevision();
        return definition with
        {
            ContainerRegistry = NormalizeContainerRegistry(definition.ContainerRegistry),
            ContainerRegistryCredentials = ContainerRegistryCredentials.Normalize(definition.ContainerRegistryCredentials),
            ContainerRevision = containerRevision,
            ContainerRevisions = NormalizeContainerRevisions(definition, containerRevision),
            ReplicasEnabled = replicasEnabled
        };
    }

    private static IReadOnlyList<ApplicationContainerRevision> NormalizeContainerRevisions(
        ApplicationResourceDefinition definition,
        string containerRevision)
    {
        var revisions = definition.ContainerRevisions
            .Where(revision => !string.IsNullOrWhiteSpace(revision.Id))
            .Select(revision => revision with
            {
                Id = revision.Id.Trim(),
                Image = NormalizeNullable(revision.Image) ?? NormalizeNullable(definition.ContainerImage) ?? "unresolved",
                RequestedReplicas = Math.Max(1, revision.RequestedReplicas),
                ChangeKind = NormalizeNullable(revision.ChangeKind) ?? ApplicationContainerRevisionChangeKinds.ImageDeployment,
                SourceRevisionId = NormalizeNullable(revision.SourceRevisionId),
                TriggeredBy = NormalizeNullable(revision.TriggeredBy)
            })
            .DistinctBy(revision => revision.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (revisions.Any(revision => string.Equals(revision.Id, containerRevision, StringComparison.OrdinalIgnoreCase)))
        {
            return revisions;
        }

        return
        [
            ..revisions,
            new ApplicationContainerRevision(
                containerRevision,
                NormalizeNullable(definition.ContainerImage) ?? "unresolved",
                Math.Max(1, definition.Replicas),
                DateTimeOffset.UtcNow,
                ApplicationContainerRevisionChangeKinds.Initial)
        ];
    }

    private static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        !string.IsNullOrWhiteSpace(application.ContainerImage) ||
        application.ProjectContainerBuild ||
        !string.IsNullOrWhiteSpace(application.ContainerBuildContext);

    private static string NormalizeContainerRegistry(string? registry) =>
        NormalizeNullable(registry) ?? ContainerRegistryDefaults.Default;

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateContainerRevision() =>
        $"rev-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..27];
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
