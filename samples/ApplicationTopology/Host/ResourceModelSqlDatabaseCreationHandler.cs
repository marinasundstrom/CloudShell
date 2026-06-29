using CloudShell.ResourceModel;
using CloudShell.ResourceModel.ReferenceProviders;
using Microsoft.Data.SqlClient;

namespace CloudShell.ApplicationTopologyHost;

internal sealed class ResourceModelSqlDatabaseCreationHandler(
    IConfiguration configuration) : ISqlDatabaseCreationHandler
{
    private readonly IConfiguration _configuration =
        configuration ?? throw new ArgumentNullException(nameof(configuration));

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EnsureCreatedAsync(
        SqlDatabaseCreationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            var databaseName = context.Database.Attributes.GetString(
                SqlDatabaseResourceTypeProvider.Attributes.DatabaseName);
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "applicationTopology.sqlDatabase.nameRequired",
                        "The Resource model SQL database name is required.",
                        context.Database.EffectiveResourceId)
                ];
            }

            if (databaseName.Length > 128)
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "applicationTopology.sqlDatabase.nameTooLong",
                        "The Resource model SQL database name cannot be longer than 128 characters.",
                        context.Database.EffectiveResourceId)
                ];
            }

            if (!ResourceModelSqlServerConnectionSupport.TryCreateAdministratorConnectionString(
                    context.Server,
                    _configuration,
                    "master",
                    out var connectionString))
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "applicationTopology.sqlServer.connectionUnavailable",
                        "The Resource model SQL Server endpoint or administrator password is not available.",
                        context.Server.EffectiveResourceId)
                ];
            }

            await using var connection = await ResourceModelSqlServerConnectionSupport.OpenWithRetryAsync(
                context.Server,
                connectionString,
                cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF DB_ID(@databaseName) IS NULL
                BEGIN
                    DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@databaseName);
                    EXEC sp_executesql @sql;
                END
                """;
            command.Parameters.AddWithValue("@databaseName", databaseName.Trim());
            await command.ExecuteNonQueryAsync(cancellationToken);

            return [];
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "applicationTopology.sqlDatabase.ensureCreatedFailed",
                    exception.Message,
                    context.Database.EffectiveResourceId)
            ];
        }
    }
}
