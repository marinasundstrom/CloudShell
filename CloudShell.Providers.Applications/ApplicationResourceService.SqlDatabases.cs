using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private static IReadOnlyList<Resource> CreateSqlDatabaseResources(ApplicationResourceDefinition application) =>
        application.SqlDatabases
            .Select(database => CreateSqlDatabaseResource(application, database))
            .ToArray();

    private static Resource CreateSqlDatabaseResource(
        ApplicationResourceDefinition application,
        SqlServerDatabaseDefinition database)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DatabaseName] = database.Name,
            [ResourceAttributeNames.DatabaseServerResourceId] = application.Id,
            [ResourceAttributeNames.DatabaseSource] = "declared"
        };

        return new Resource(
            CreateSqlDatabaseResourceId(application.Id, database.Name),
            database.Name,
            "SQL database",
            "Applications",
            "local",
            null,
            [],
            ApplicationResourceProjectionSupport.GetContainerVersion(application) ?? string.Empty,
            DateTimeOffset.UtcNow,
            [application.Id],
            ParentResourceId: application.Id,
            TypeId: ApplicationResourceTypes.SqlDatabase,
            ResourceClass: ResourceClass.Service,
            Attributes: attributes,
            Source: ResourceSource.Provider,
            ManagementMode: ResourceManagementMode.ProviderManaged,
            Visibility: ResourceVisibility.Diagnostic,
            OwnerResourceId: application.Id,
            CleanupBehavior: ResourceCleanupBehavior.DeleteWithOwner,
            DisplayName: string.IsNullOrWhiteSpace(database.DisplayName)
                ? database.Name
                : database.DisplayName);
    }

    private static IReadOnlyList<SqlServerDatabaseDefinition> NormalizeSqlDatabases(
        IReadOnlyList<SqlServerDatabaseDefinition> databases) =>
        databases
            .Where(database => !string.IsNullOrWhiteSpace(database.Name))
            .Select(database => new SqlServerDatabaseDefinition(
                NormalizeDatabaseName(database.Name),
                NormalizeNullable(database.DisplayName)))
            .DistinctBy(database => database.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeDatabaseName(string name) =>
        name.Trim();

    private static string CreateSqlDatabaseResourceId(string serverResourceId, string databaseName) =>
        $"{serverResourceId}/database:{CreateStableIdentifier(databaseName)}";
}
