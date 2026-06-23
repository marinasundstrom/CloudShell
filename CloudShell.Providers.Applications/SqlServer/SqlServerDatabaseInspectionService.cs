using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class SqlServerDatabaseInspectionService(
    IApplicationResourceManagementOperations applications,
    IApplicationResourceProjectionSource projections) : ISqlServerDatabaseInspectionOperations
{
    public async Task<IReadOnlyList<SqlServerDatabaseInfo>> QuerySqlServerDatabasesAsync(
        string sqlServerResourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlServerResourceId);

        var application = applications.GetApplication(sqlServerResourceId);
        if (application is null ||
            !string.Equals(application.ResourceType, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase) ||
            !applications.IsRunning(application.Id))
        {
            return [];
        }

        var server = SqlServerResourceProjection.GetProjectedServerResource(application, projections);
        if (server is null ||
            !SqlServerConnectionFactory.TryCreateAdministratorConnectionString(
                application,
                server,
                "master",
                out var connectionString))
        {
            return [];
        }

        var databases = new List<SqlServerDatabaseInfo>();
        await using var connection = await SqlServerConnectionFactory.OpenWithRetryAsync(
            application,
            connectionString,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [name], [state_desc],
                CAST(CASE WHEN [database_id] <= 4 THEN 1 ELSE 0 END AS bit) AS [is_system]
            FROM sys.databases
            ORDER BY [database_id]
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            databases.Add(new SqlServerDatabaseInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2)));
        }

        return databases;
    }

}
