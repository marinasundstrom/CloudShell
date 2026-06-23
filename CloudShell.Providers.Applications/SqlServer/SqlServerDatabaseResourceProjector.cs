using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class SqlServerDatabaseResourceProjector
{
    public static IReadOnlyList<Resource> CreateResources(ApplicationResourceDefinition application) =>
        application.SqlDatabases
            .Select(database => CreateResource(application, database))
            .ToArray();

    private static Resource CreateResource(
        ApplicationResourceDefinition application,
        SqlServerDatabaseDefinition database)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DatabaseName] = database.Name,
            [ResourceAttributeNames.DatabaseServerResourceId] = application.Id,
            [ResourceAttributeNames.DatabaseSource] = "declared",
            [ResourceAttributeNames.DatabaseEnsureCreated] = database.EnsureCreated.ToString().ToLowerInvariant()
        };

        return new Resource(
            CreateResourceId(application.Id, database.Name),
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

    private static string CreateResourceId(string serverResourceId, string databaseName) =>
        $"{serverResourceId}/database:{ApplicationResourceNames.CreateStableIdentifier(databaseName)}";
}
