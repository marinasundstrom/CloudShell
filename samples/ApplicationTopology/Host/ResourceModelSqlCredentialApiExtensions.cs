using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ResourceModelResource = CloudShell.ResourceModel.Resource;
using ResourceModelResourceState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ApplicationTopologyHost;

internal static class ResourceModelSqlCredentialApiExtensions
{
    public static RouteGroupBuilder MapApplicationTopologyResourceModelSqlCredentialApi(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints
            .MapGroup("/api/application-topology/sql-server/v1")
            .WithTags("ApplicationTopology SQL Server");

        api.MapPost("/credentials", ResolveCredential)
            .WithName("ApplicationTopologyResourceModelSqlServer_ResolveCredential")
            .Accepts<ResolveSqlServerCredentialRequest>("application/json")
            .Produces<ResolveSqlServerCredentialResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        return api;
    }

    private static async Task<IResult> ResolveCredential(
        ResolveSqlServerCredentialRequest request,
        HttpContext httpContext,
        ResourceGraphModel graphModel,
        ResourceGraphResolver graphResolver,
        ISqlDatabaseCreationHandler databaseCreationHandler,
        IConfiguration configuration,
        IResourceEventSink? resourceEvents,
        CancellationToken cancellationToken)
    {
        try
        {
            var permission = string.IsNullOrWhiteSpace(request.Permission)
                ? DatabaseResourceOperationPermissions.ReadWrite
                : request.Permission.Trim();
            var result = await ResolveCredentialAsync(
                request,
                permission,
                httpContext.User,
                graphModel,
                graphResolver,
                databaseCreationHandler,
                configuration,
                resourceEvents,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(
                title: "SQL Server credential access denied",
                detail: exception.Message,
                statusCode: httpContext.User.Identity?.IsAuthenticated == true
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status401Unauthorized);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.Problem(
                title: "SQL Server credential request invalid",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (SqlException exception)
        {
            return Results.Problem(
                title: "SQL Server credential resolution failed",
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<ResolveSqlServerCredentialResponse> ResolveCredentialAsync(
        ResolveSqlServerCredentialRequest request,
        string permission,
        ClaimsPrincipal principal,
        ResourceGraphModel graphModel,
        ResourceGraphResolver graphResolver,
        ISqlDatabaseCreationHandler databaseCreationHandler,
        IConfiguration configuration,
        IResourceEventSink? resourceEvents,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SqlServerResourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DatabaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("A CloudShell resource identity token is required.");
        }

        var snapshot = await graphModel.GetSnapshotAsync(cancellationToken);
        var serverState = snapshot.Resources.FirstOrDefault(resource =>
            resource.TypeId == SqlServerResourceTypeProvider.ResourceTypeId &&
            (string.Equals(resource.EffectiveResourceId, request.SqlServerResourceName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(resource.Name, request.SqlServerResourceName, StringComparison.OrdinalIgnoreCase)));
        if (serverState is null)
        {
            throw new InvalidOperationException(
                $"SQL Server resource '{request.SqlServerResourceName}' could not be found.");
        }

        var resolution = graphResolver.ResolveResource(snapshot, serverState.EffectiveResourceId);
        if (resolution.HasErrors || resolution.Resource is null)
        {
            throw new InvalidOperationException(
                $"SQL Server resource '{serverState.EffectiveResourceId}' could not be resolved.");
        }

        var server = resolution.Resource;
        var subject = GetPrincipalSubject(principal);
        var databaseName = request.DatabaseName.Trim();
        if (!TryGetDeclaredDatabase(snapshot.Resources, server.EffectiveResourceId, databaseName, out var databaseState))
        {
            AppendCredentialEvent(
                resourceEvents,
                server,
                "credential.request.denied",
                $"Credential request for database '{databaseName}' was denied because the graph database is not declared.",
                subject,
                ResourceSignalSeverity.Warning);
            throw new InvalidOperationException(
                $"SQL Server resource '{server.Name}' does not declare database '{databaseName}'.");
        }

        if (!ResourcePermissionClaimAuthorization.HasResourcePermission(
                principal,
                server.EffectiveResourceId,
                permission))
        {
            AppendCredentialEvent(
                resourceEvents,
                server,
                "credential.request.denied",
                $"Credential request for database '{databaseName}' was denied because principal '{subject}' does not have '{permission}'.",
                subject,
                ResourceSignalSeverity.Warning);
            throw new UnauthorizedAccessException(
                $"The current CloudShell principal cannot resolve Resource model SQL credentials for resource '{server.Name}'.");
        }

        await EnsureDeclaredDatabaseCreatedAsync(
            databaseState,
            server,
            graphResolver,
            snapshot,
            databaseCreationHandler,
            cancellationToken);

        if (!ResourceModelSqlServerConnectionSupport.TryCreateAdministratorConnectionString(
                server,
                configuration,
                "master",
                out var masterConnectionString))
        {
            AppendCredentialEvent(
                resourceEvents,
                server,
                "credential.request.failed",
                $"Credential request for database '{databaseName}' failed because the Resource model SQL Server endpoint or administrator password is not available.",
                subject,
                ResourceSignalSeverity.Warning);
            throw new InvalidOperationException(
                $"SQL Server resource '{server.Name}' cannot resolve credentials because its TDS endpoint or administrator password is not available.");
        }

        var userName = CreateManagedUserName(subject, server.EffectiveResourceId, permission);
        var password = CreateCredentialPassword();
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(15);

        await EnsureLoginAsync(
            server,
            masterConnectionString,
            userName,
            password,
            cancellationToken);
        await EnsureDatabaseUserAsync(
            server,
            configuration,
            databaseName,
            userName,
            cancellationToken);

        if (!ResourceModelSqlServerConnectionSupport.TryCreateCredentialConnectionString(
                server,
                databaseName,
                userName,
                password,
                out var connectionString))
        {
            throw new InvalidOperationException(
                $"SQL Server resource '{server.Name}' cannot create a credential connection string.");
        }

        AppendCredentialEvent(
            resourceEvents,
            server,
            "credential.resolved",
            $"Credential resolved for database '{databaseName}' for principal '{subject}' with permission '{permission}'.",
            subject,
            ResourceSignalSeverity.Info);

        return new ResolveSqlServerCredentialResponse(connectionString, expiresOn);
    }

    private static bool TryGetDeclaredDatabase(
        IReadOnlyList<ResourceModelResourceState> resources,
        string serverResourceId,
        string databaseName,
        out ResourceModelResourceState database)
    {
        database = resources.FirstOrDefault(resource =>
            resource.TypeId == SqlDatabaseResourceTypeProvider.ResourceTypeId &&
            TryGetDatabaseName(resource, out var declaredDatabaseName) &&
            string.Equals(declaredDatabaseName, databaseName, StringComparison.OrdinalIgnoreCase) &&
            SqlDatabaseResourceTypeProvider.TryGetServerDependencyResourceId(
                resource,
                out var declaredServerResourceId) &&
            string.Equals(declaredServerResourceId, serverResourceId, StringComparison.OrdinalIgnoreCase))!;

        return database is not null;
    }

    private static async Task EnsureDeclaredDatabaseCreatedAsync(
        ResourceModelResourceState databaseState,
        ResourceModelResource server,
        ResourceGraphResolver graphResolver,
        ResourceGraphSnapshot snapshot,
        ISqlDatabaseCreationHandler databaseCreationHandler,
        CancellationToken cancellationToken)
    {
        if (!ShouldEnsureCreated(databaseState))
        {
            return;
        }

        var databaseResolution = graphResolver.ResolveResource(snapshot, databaseState.EffectiveResourceId);
        if (databaseResolution.HasErrors || databaseResolution.Resource is null)
        {
            throw new InvalidOperationException(
                $"SQL database resource '{databaseState.EffectiveResourceId}' could not be resolved.");
        }

        var diagnostics = await databaseCreationHandler.EnsureCreatedAsync(
            new SqlDatabaseCreationContext(databaseResolution.Resource, server),
            cancellationToken);
        var error = diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
        if (error is not null)
        {
            throw new InvalidOperationException(error.Message);
        }
    }

    private static bool ShouldEnsureCreated(ResourceModelResourceState databaseState) =>
        databaseState.ResourceAttributeValues.TryGetValue(
            SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated,
            out var value) &&
        value.TryGetScalarString(out var ensureCreated) &&
        bool.TryParse(ensureCreated, out var parsed) &&
        parsed;

    private static bool TryGetDatabaseName(
        ResourceModelResourceState resource,
        out string databaseName)
    {
        databaseName = string.Empty;
        return resource.ResourceAttributeValues.TryGetValue(
                SqlDatabaseResourceTypeProvider.Attributes.DatabaseName,
                out var value) &&
            value.TryGetScalarString(out databaseName);
    }

    private static async Task EnsureLoginAsync(
        ResourceModelResource server,
        string masterConnectionString,
        string loginName,
        string password,
        CancellationToken cancellationToken)
    {
        await using var connection = await ResourceModelSqlServerConnectionSupport.OpenWithRetryAsync(
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
        ResourceModelResource server,
        IConfiguration configuration,
        string databaseName,
        string userName,
        CancellationToken cancellationToken)
    {
        if (!ResourceModelSqlServerConnectionSupport.TryCreateAdministratorConnectionString(
                server,
                configuration,
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

    private static string GetPrincipalSubject(ClaimsPrincipal principal)
    {
        var subject =
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
            principal.FindFirstValue("sub") ??
            principal.Identity?.Name;

        return string.IsNullOrWhiteSpace(subject)
            ? throw new UnauthorizedAccessException("The CloudShell resource identity token does not include a subject.")
            : subject;
    }

    private static string CreateManagedUserName(
        string subject,
        string targetResourceId,
        string permission)
    {
        var key = $"{subject}\u001f{targetResourceId}\u001f{permission}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);
        return $"cloudshell_{Convert.ToHexString(hash[..10]).ToLowerInvariant()}";
    }

    private static string CreateCredentialPassword()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return $"Cs1!{Convert.ToBase64String(bytes).Replace('+', 'A').Replace('/', 'b')}";
    }

    private static void AppendCredentialEvent(
        IResourceEventSink? resourceEvents,
        ResourceModelResource server,
        string eventName,
        string message,
        string? triggeredBy,
        ResourceSignalSeverity severity)
    {
        resourceEvents?.Append(new ResourceEvent(
            server.EffectiveResourceId,
            ResourceEventTypes.Events.Provider.ForEvent(SqlServerResourceTypeProvider.ProviderId, eventName),
            message,
            DateTimeOffset.UtcNow,
            TriggeredBy: triggeredBy,
            Severity: severity));
    }
}

public sealed record ResolveSqlServerCredentialRequest(
    string SqlServerResourceName,
    string DatabaseName,
    string? Permission = null);

public sealed record ResolveSqlServerCredentialResponse(
    string ConnectionString,
    DateTimeOffset? ExpiresOn = null);
