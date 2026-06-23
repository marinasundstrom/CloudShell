using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class SqlServerGrantStatusService(
    IApplicationResourceManagementOperations applications,
    IApplicationResourceProjectionSource projections) : ISqlServerApplicationResourceProviderOperations
{
    public async Task<ResourcePermissionGrantStatus> GetSqlServerPermissionGrantStatusAsync(
        ResourcePermissionGrantStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var application = applications.GetApplication(request.TargetResource.Id);
        if (application is null ||
            !string.Equals(application.ResourceType, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            return new ResourcePermissionGrantStatus(
                request.Grant,
                ResourcePermissionGrantEffectivenessState.Unknown,
                "The SQL Server resource is not configured.",
                ApplicationResourceProviderIds.SqlServer,
                observedAt);
        }

        if (request.Grant.ResourceIdentity is null)
        {
            return new ResourcePermissionGrantStatus(
                request.Grant,
                ResourcePermissionGrantEffectivenessState.NotApplied,
                "The local SQL Server provider can currently materialize database grants only for resource identity principals.",
                ApplicationResourceProviderIds.SqlServer,
                observedAt);
        }

        if (application.SqlDatabases.Count == 0)
        {
            return new ResourcePermissionGrantStatus(
                request.Grant,
                ResourcePermissionGrantEffectivenessState.NotApplied,
                "The SQL Server resource has no declared databases to apply this grant to.",
                ApplicationResourceProviderIds.SqlServer,
                observedAt);
        }

        if (!applications.IsRunning(application.Id))
        {
            return new ResourcePermissionGrantStatus(
                request.Grant,
                ResourcePermissionGrantEffectivenessState.Pending,
                "Start SQL Server to inspect or reconcile database users and roles.",
                ApplicationResourceProviderIds.SqlServer,
                observedAt);
        }

        var serverResource = SqlServerResourceProjection.GetProjectedServerResource(application, projections);
        if (serverResource is null ||
            !SqlServerConnectionFactory.TryCreateAdministratorConnectionString(application, serverResource, "master", out _))
        {
            return new ResourcePermissionGrantStatus(
                request.Grant,
                ResourcePermissionGrantEffectivenessState.Pending,
                "The SQL Server TDS endpoint or administrator password is not available.",
                ApplicationResourceProviderIds.SqlServer,
                observedAt);
        }

        try
        {
            var userName = SqlServerCredentialNames.CreateManagedUserName(request.Grant);
            var missingDatabases = new List<string>();
            foreach (var database in application.SqlDatabases)
            {
                var materialized = await IsDatabaseGrantMaterializedAsync(
                    application,
                    serverResource,
                    database.Name,
                    userName,
                    cancellationToken);
                if (!materialized)
                {
                    missingDatabases.Add(database.Name);
                }
            }

            return missingDatabases.Count == 0
                ? new ResourcePermissionGrantStatus(
                    request.Grant,
                    ResourcePermissionGrantEffectivenessState.Applied,
                    "SQL Server database users and read/write role memberships are present for declared databases. Provider-owned credential delivery is available through the SQL Server credential broker.",
                    ApplicationResourceProviderIds.SqlServer,
                    observedAt)
                : new ResourcePermissionGrantStatus(
                    request.Grant,
                    ResourcePermissionGrantEffectivenessState.Drifted,
                    $"SQL Server database access is missing for {FormatDatabaseList(missingDatabases)}.",
                    ApplicationResourceProviderIds.SqlServer,
                    observedAt);
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            return new ResourcePermissionGrantStatus(
                request.Grant,
                ResourcePermissionGrantEffectivenessState.Failed,
                exception.Message,
                ApplicationResourceProviderIds.SqlServer,
                observedAt);
        }
    }

    private static async Task<bool> IsDatabaseGrantMaterializedAsync(
        ApplicationResourceDefinition server,
        Resource serverResource,
        string databaseName,
        string userName,
        CancellationToken cancellationToken)
    {
        if (!SqlServerConnectionFactory.TryCreateAdministratorConnectionString(
                server,
                serverResource,
                databaseName,
                out var connectionString))
        {
            return false;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                CAST(CASE WHEN USER_ID(@userName) IS NULL THEN 0 ELSE 1 END AS bit),
                CAST(CASE WHEN ISNULL(IS_ROLEMEMBER(N'db_datareader', @userName), 0) = 1 THEN 1 ELSE 0 END AS bit),
                CAST(CASE WHEN ISNULL(IS_ROLEMEMBER(N'db_datawriter', @userName), 0) = 1 THEN 1 ELSE 0 END AS bit)
            """;
        command.Parameters.AddWithValue("@userName", userName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) &&
            reader.GetBoolean(0) &&
            reader.GetBoolean(1) &&
            reader.GetBoolean(2);
    }

    private static string FormatDatabaseList(IReadOnlyList<string> databases) =>
        databases.Count == 1
            ? $"database '{databases[0]}'"
            : $"{databases.Count.ToString(CultureInfo.InvariantCulture)} databases: {string.Join(", ", databases.Select(database => $"'{database}'"))}";
}
