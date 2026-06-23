using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class SqlServerDatabaseReconciliationService(
    ApplicationResourceStore store,
    ResourceDeclarationStore declarations,
    IApplicationResourceProjectionSource projections)
{
    public async Task<int> ReconcileAsync(
        string sqlServerResourceId,
        string providerId,
        ResourceProcedureContext? procedureContext,
        CancellationToken cancellationToken)
    {
        var definition = store.GetApplication(sqlServerResourceId);
        if (definition is null ||
            !string.Equals(definition.ResourceType, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (definition.SqlDatabases.Count == 0)
        {
            return 0;
        }

        var ensureCreatedDatabases = definition.SqlDatabases
            .Where(database => database.EnsureCreated)
            .ToArray();
        var grants = declarations
            .GetPermissionGrants()
            .Where(grant =>
                string.Equals(grant.TargetResourceId, definition.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(grant.Permission, DatabaseResourceOperationPermissions.ReadWrite, StringComparison.OrdinalIgnoreCase) &&
                grant.ResourceIdentity is not null)
            .ToArray();

        if (ensureCreatedDatabases.Length == 0 &&
            grants.Length == 0)
        {
            return 0;
        }

        var serverResource = SqlServerResourceProjection.GetProjectedServerResource(definition, projections);
        if (serverResource is null ||
            !SqlServerConnectionFactory.TryCreateAdministratorConnectionString(
                definition,
                serverResource,
                "master",
                out var masterConnectionString))
        {
            throw new InvalidOperationException(
                $"SQL Server resource '{definition.Name}' cannot reconcile databases because its TDS endpoint or administrator password is not available.");
        }

        if (ensureCreatedDatabases.Length > 0)
        {
            await ReconcileEnsureCreatedDatabasesAsync(
                definition,
                masterConnectionString,
                ensureCreatedDatabases,
                providerId,
                procedureContext,
                cancellationToken);
        }

        procedureContext?.AppendProviderEvent(
            providerId,
            "application.sql.access.reconciling",
            $"Application provider is reconciling {grants.Length.ToString(CultureInfo.InvariantCulture)} SQL Server database access grant{Pluralize(grants.Length)} for '{definition.Name}'.");

        var managedUsers = grants
            .Select(SqlServerCredentialNames.CreateManagedUserName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var database in definition.SqlDatabases)
        {
            await ReconcileDatabaseAccessAsync(
                definition,
                serverResource,
                database.Name,
                managedUsers,
                cancellationToken);
        }

        procedureContext?.AppendProviderEvent(
            providerId,
            "application.sql.access.reconciled",
            $"Application provider reconciled SQL Server database access grants for '{definition.Name}'.");

        return grants.Length;
    }

    private static async Task ReconcileEnsureCreatedDatabasesAsync(
        ApplicationResourceDefinition definition,
        string masterConnectionString,
        IReadOnlyList<SqlServerDatabaseDefinition> databases,
        string providerId,
        ResourceProcedureContext? procedureContext,
        CancellationToken cancellationToken)
    {
        foreach (var database in databases)
        {
            if (database.Name.Length > 128)
            {
                throw new InvalidOperationException(
                    $"SQL Server resource '{definition.Name}' declares database '{database.Name}' with a name longer than 128 characters.");
            }
        }

        procedureContext?.AppendProviderEvent(
            providerId,
            "application.sql.databases.reconciling",
            $"Application provider is ensuring {databases.Count.ToString(CultureInfo.InvariantCulture)} SQL database{Pluralize(databases.Count)} exist for '{definition.Name}'.");

        await using var connection = await SqlServerConnectionFactory.OpenWithRetryAsync(
            definition,
            masterConnectionString,
            cancellationToken);

        foreach (var database in databases)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF DB_ID(@databaseName) IS NULL
                BEGIN
                    DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@databaseName);
                    EXEC sp_executesql @sql;
                END
                """;
            command.Parameters.AddWithValue("@databaseName", database.Name);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        procedureContext?.AppendProviderEvent(
            providerId,
            "application.sql.databases.reconciled",
            $"Application provider ensured declared SQL databases exist for '{definition.Name}'.");
    }

    private static async Task ReconcileDatabaseAccessAsync(
        ApplicationResourceDefinition server,
        Resource serverResource,
        string databaseName,
        IReadOnlyCollection<string> managedUsers,
        CancellationToken cancellationToken)
    {
        if (!SqlServerConnectionFactory.TryCreateAdministratorConnectionString(
                server,
                serverResource,
                databaseName,
                out var connectionString))
        {
            throw new InvalidOperationException(
                $"SQL Server resource '{server.Name}' cannot reconcile database access for '{databaseName}' because its TDS endpoint or administrator password is not available.");
        }

        await using var connection = await SqlServerConnectionFactory.OpenWithRetryAsync(
            server,
            connectionString,
            cancellationToken);

        foreach (var userName in managedUsers)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF USER_ID(@userName) IS NULL
                BEGIN
                    DECLARE @createUserSql nvarchar(max) = N'CREATE USER ' + QUOTENAME(@userName) + N' WITHOUT LOGIN';
                    EXEC sp_executesql @createUserSql;
                END

                IF ISNULL(IS_ROLEMEMBER(N'db_datareader', @userName), 0) <> 1
                BEGIN
                    DECLARE @readerSql nvarchar(max) = N'ALTER ROLE [db_datareader] ADD MEMBER ' + QUOTENAME(@userName);
                    EXEC sp_executesql @readerSql;
                END

                IF ISNULL(IS_ROLEMEMBER(N'db_datawriter', @userName), 0) <> 1
                BEGIN
                    DECLARE @writerSql nvarchar(max) = N'ALTER ROLE [db_datawriter] ADD MEMBER ' + QUOTENAME(@userName);
                    EXEC sp_executesql @writerSql;
                END
                """;
            command.Parameters.AddWithValue("@userName", userName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await RemoveOrphanedManagedUsersAsync(
            connection,
            managedUsers,
            cancellationToken);
    }

    private static async Task RemoveOrphanedManagedUsersAsync(
        SqlConnection connection,
        IReadOnlyCollection<string> currentManagedUsers,
        CancellationToken cancellationToken)
    {
        var existingUsers = new List<string>();
        await using (var listCommand = connection.CreateCommand())
        {
            listCommand.CommandText = """
                SELECT [name]
                FROM sys.database_principals
                WHERE [type] = 'S'
                    AND [authentication_type_desc] = 'NONE'
                    AND [name] LIKE @prefix
                """;
            listCommand.Parameters.AddWithValue("@prefix", "cloudshell[_]%");
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existingUsers.Add(reader.GetString(0));
            }
        }

        foreach (var userName in existingUsers)
        {
            if (currentManagedUsers.Contains(userName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF ISNULL(IS_ROLEMEMBER(N'db_datareader', @userName), 0) = 1
                BEGIN
                    DECLARE @readerSql nvarchar(max) = N'ALTER ROLE [db_datareader] DROP MEMBER ' + QUOTENAME(@userName);
                    EXEC sp_executesql @readerSql;
                END

                IF ISNULL(IS_ROLEMEMBER(N'db_datawriter', @userName), 0) = 1
                BEGIN
                    DECLARE @writerSql nvarchar(max) = N'ALTER ROLE [db_datawriter] DROP MEMBER ' + QUOTENAME(@userName);
                    EXEC sp_executesql @writerSql;
                END

                IF USER_ID(@userName) IS NOT NULL
                BEGIN
                    DECLARE @dropUserSql nvarchar(max) = N'DROP USER ' + QUOTENAME(@userName);
                    EXEC sp_executesql @dropUserSql;
                END
                """;
            command.Parameters.AddWithValue("@userName", userName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string Pluralize(int count) =>
        count == 1 ? string.Empty : "s";
}
