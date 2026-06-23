namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private static IReadOnlyList<SqlServerDatabaseDefinition> NormalizeSqlDatabases(
        IReadOnlyList<SqlServerDatabaseDefinition> databases) =>
        databases
            .Where(database => !string.IsNullOrWhiteSpace(database.Name))
            .Select(database => new SqlServerDatabaseDefinition(
                NormalizeDatabaseName(database.Name),
                NormalizeNullable(database.DisplayName),
                database.EnsureCreated))
            .DistinctBy(database => database.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeDatabaseName(string name) =>
        name.Trim();
}
