using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;

namespace CloudShell.ApplicationTopologyHost;

internal sealed class GraphSqlDatabaseCreationHandler(
    IConfiguration configuration) : ISqlDatabaseCreationHandler
{
    private static readonly TimeSpan ConnectionRetryTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(1);

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
                        "The graph SQL database name is required.",
                        context.Database.EffectiveResourceId)
                ];
            }

            if (databaseName.Length > 128)
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "applicationTopology.sqlDatabase.nameTooLong",
                        "The graph SQL database name cannot be longer than 128 characters.",
                        context.Database.EffectiveResourceId)
                ];
            }

            if (!TryCreateMasterConnectionString(
                    context.Server,
                    out var connectionString))
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "applicationTopology.sqlServer.connectionUnavailable",
                        "The graph SQL Server endpoint or administrator password is not available.",
                        context.Server.EffectiveResourceId)
                ];
            }

            await using var connection = await OpenWithRetryAsync(
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

    private bool TryCreateMasterConnectionString(
        Resource server,
        out string connectionString)
    {
        connectionString = string.Empty;

        var endpoint = server.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                SqlServerResourceTypeProvider.Attributes.EndpointRequests)?
            .FirstOrDefault(endpoint =>
                string.Equals(endpoint.Name, "tds", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(endpoint.Protocol, "tcp", StringComparison.OrdinalIgnoreCase));
        if (endpoint is null ||
            string.IsNullOrWhiteSpace(endpoint.Host) ||
            endpoint.Port is not > 0)
        {
            return false;
        }

        var password = _configuration["ApplicationTopology:SqlServer:Password"] ??
            ApplicationProviderServiceCollectionExtensions.DefaultSqlServerAdministratorPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        connectionString = new SqlConnectionStringBuilder
        {
            DataSource = $"{endpoint.Host.Trim()},{endpoint.Port.Value.ToString(CultureInfo.InvariantCulture)}",
            InitialCatalog = "master",
            UserID = "sa",
            Password = password,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        }.ConnectionString;
        return true;
    }

    private static async Task<SqlConnection> OpenWithRetryAsync(
        Resource server,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;

        while (stopwatch.Elapsed < ConnectionRetryTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (SqlException exception) when (ShouldRetryConnection(exception))
            {
                lastError = exception;
                await connection.DisposeAsync();
            }

            var remaining = ConnectionRetryTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(
                remaining < ConnectionRetryDelay
                    ? remaining
                    : ConnectionRetryDelay,
                cancellationToken);
        }

        throw new InvalidOperationException(
            $"Graph SQL Server resource '{server.Name}' did not accept connections within {ConnectionRetryTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
            lastError);
    }

    private static bool ShouldRetryConnection(SqlException exception)
    {
        foreach (SqlError error in exception.Errors)
        {
            if (error.Number == 4060)
            {
                return false;
            }
        }

        return true;
    }
}
