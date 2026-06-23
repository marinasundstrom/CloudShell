using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace CloudShell.Providers.Applications;

internal sealed class SqlServerCredentialResolutionService(
    IApplicationResourceManagementOperations applications,
    IApplicationResourceProjectionSource projections,
    IResourceEventSink? resourceEvents = null) : ISqlServerCredentialResolutionOperations
{
    public async Task<SqlServerCredentialResolutionResult> ResolveSqlServerCredentialAsync(
        string sqlServerResourceName,
        string databaseName,
        string permission,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlServerResourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("A CloudShell resource identity token is required.");
        }

        var application = ResolveSqlServerApplication(sqlServerResourceName);
        if (application is null)
        {
            throw new InvalidOperationException(
                $"SQL Server resource '{sqlServerResourceName}' could not be found.");
        }

        var subject = SqlServerCredentialNames.GetPrincipalSubject(principal);

        if (!application.SqlDatabases.Any(database =>
                string.Equals(database.Name, databaseName, StringComparison.OrdinalIgnoreCase)))
        {
            AppendCredentialEvent(
                application,
                "credential.request.denied",
                $"Credential request for database '{databaseName}' was denied because the database is not declared.",
                subject,
                ResourceSignalSeverity.Warning);
            throw new InvalidOperationException(
                $"SQL Server resource '{application.Name}' does not declare database '{databaseName}'.");
        }

        if (!ResourcePermissionClaimAuthorization.HasResourcePermission(
                principal,
                application.Id,
                permission))
        {
            AppendCredentialEvent(
                application,
                "credential.request.denied",
                $"Credential request for database '{databaseName}' was denied because principal '{subject}' does not have '{permission}'.",
                subject,
                ResourceSignalSeverity.Warning);
            throw new UnauthorizedAccessException(
                $"The current CloudShell principal cannot resolve SQL credentials for resource '{application.Name}'.");
        }

        if (!applications.IsRunning(application.Id))
        {
            AppendCredentialEvent(
                application,
                "credential.request.failed",
                $"Credential request for database '{databaseName}' failed because the SQL Server resource is not running.",
                subject,
                ResourceSignalSeverity.Warning);
            throw new InvalidOperationException(
                $"SQL Server resource '{application.Name}' must be running before credentials can be resolved.");
        }

        var serverResource = SqlServerResourceProjection.GetProjectedServerResource(application, projections);
        if (serverResource is null ||
            !serverResource.TryGetResolvedEndpointUri("tds", out var endpoint) ||
            !SqlServerConnectionFactory.TryCreateAdministratorConnectionString(
                application,
                serverResource,
                "master",
                out var masterConnectionString))
        {
            AppendCredentialEvent(
                application,
                "credential.request.failed",
                $"Credential request for database '{databaseName}' failed because the TDS endpoint or administrator password is not available.",
                subject,
                ResourceSignalSeverity.Warning);
            throw new InvalidOperationException(
                $"SQL Server resource '{application.Name}' cannot resolve credentials because its TDS endpoint or administrator password is not available.");
        }

        var userName = SqlServerCredentialNames.CreateManagedUserNameFromPrincipalSubject(
            subject,
            application.Id,
            permission);
        var password = SqlServerCredentialNames.CreateCredentialPassword();
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(15);

        await EnsureLoginAsync(
            application,
            masterConnectionString,
            userName,
            password,
            cancellationToken);
        await EnsureDatabaseUserAsync(
            application,
            serverResource,
            databaseName,
            userName,
            cancellationToken);

        var connectionString = SqlServerConnectionFactory.CreateConnectionString(
            endpoint,
            databaseName.Trim(),
            userName,
            password);

        AppendCredentialEvent(
            application,
            "credential.resolved",
            $"Credential resolved for database '{databaseName}' for principal '{subject}' with permission '{permission}'.",
            subject,
            ResourceSignalSeverity.Info);

        return new SqlServerCredentialResolutionResult(connectionString, expiresOn);
    }

    private ApplicationResourceDefinition? ResolveSqlServerApplication(string resourceNameOrId) =>
        applications
            .GetApplications()
            .FirstOrDefault(application =>
                string.Equals(application.ResourceType, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(application.Id, resourceNameOrId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(application.Name, resourceNameOrId, StringComparison.OrdinalIgnoreCase)));

    private void AppendCredentialEvent(
        ApplicationResourceDefinition application,
        string eventName,
        string message,
        string? triggeredBy,
        ResourceSignalSeverity severity)
    {
        resourceEvents?.Append(new ResourceEvent(
            application.Id,
            ResourceEventTypes.Events.Provider.ForEvent(ApplicationResourceProviderIds.SqlServer, eventName),
            message,
            DateTimeOffset.UtcNow,
            TriggeredBy: triggeredBy,
            Severity: severity));
    }

    private static async Task EnsureLoginAsync(
        ApplicationResourceDefinition server,
        string masterConnectionString,
        string loginName,
        string password,
        CancellationToken cancellationToken)
    {
        await using var connection = await SqlServerConnectionFactory.OpenWithRetryAsync(
            server,
            masterConnectionString,
            cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @sql nvarchar(max);

            IF SUSER_ID(@loginName) IS NULL
            BEGIN
                SET @sql = N'CREATE LOGIN ' + QUOTENAME(@loginName) +
                    N' WITH PASSWORD = ' + QUOTENAME(@password, '''') +
                    N', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF';
            END
            ELSE
            BEGIN
                SET @sql = N'ALTER LOGIN ' + QUOTENAME(@loginName) +
                    N' WITH PASSWORD = ' + QUOTENAME(@password, '''') +
                    N', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF';
            END

            EXEC sp_executesql @sql;
            """;
        command.Parameters.AddWithValue("@loginName", loginName);
        command.Parameters.AddWithValue("@password", password);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureDatabaseUserAsync(
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
            throw new InvalidOperationException(
                $"SQL Server resource '{server.Name}' cannot map database access for '{databaseName}' because its TDS endpoint or administrator password is not available.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @authenticationType nvarchar(60) =
            (
                SELECT [authentication_type_desc]
                FROM sys.database_principals
                WHERE [name] = @userName
            );

            IF @authenticationType = N'NONE'
            BEGIN
                IF ISNULL(IS_ROLEMEMBER(N'db_datareader', @userName), 0) = 1
                BEGIN
                    DECLARE @dropReaderSql nvarchar(max) = N'ALTER ROLE [db_datareader] DROP MEMBER ' + QUOTENAME(@userName);
                    EXEC sp_executesql @dropReaderSql;
                END

                IF ISNULL(IS_ROLEMEMBER(N'db_datawriter', @userName), 0) = 1
                BEGIN
                    DECLARE @dropWriterSql nvarchar(max) = N'ALTER ROLE [db_datawriter] DROP MEMBER ' + QUOTENAME(@userName);
                    EXEC sp_executesql @dropWriterSql;
                END

                DECLARE @dropUserSql nvarchar(max) = N'DROP USER ' + QUOTENAME(@userName);
                EXEC sp_executesql @dropUserSql;
            END

            IF USER_ID(@userName) IS NULL
            BEGIN
                DECLARE @createUserSql nvarchar(max) =
                    N'CREATE USER ' + QUOTENAME(@userName) + N' FOR LOGIN ' + QUOTENAME(@userName);
                EXEC sp_executesql @createUserSql;
            END
            ELSE
            BEGIN
                DECLARE @alterUserSql nvarchar(max) =
                    N'ALTER USER ' + QUOTENAME(@userName) + N' WITH LOGIN = ' + QUOTENAME(@userName);
                EXEC sp_executesql @alterUserSql;
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
}
