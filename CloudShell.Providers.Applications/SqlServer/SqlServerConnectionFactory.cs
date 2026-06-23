using CloudShell.Abstractions.ResourceManager;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal static class SqlServerConnectionFactory
{
    private static readonly TimeSpan ConnectionRetryTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(1);

    public static bool TryCreateAdministratorConnectionString(
        ApplicationResourceDefinition server,
        Resource serverResource,
        string databaseName,
        out string connectionString)
    {
        connectionString = string.Empty;

        if (!serverResource.TryGetResolvedEndpointUri("tds", out var endpoint))
        {
            return false;
        }

        var password = GetAdministratorPassword(server);
        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        connectionString = CreateConnectionString(
            endpoint,
            string.IsNullOrWhiteSpace(databaseName) ? "master" : databaseName.Trim(),
            "sa",
            password);
        return true;
    }

    public static string CreateConnectionString(
        Uri endpoint,
        string databaseName,
        string userName,
        string password) =>
        new SqlConnectionStringBuilder
        {
            DataSource = CreateDataSource(endpoint),
            InitialCatalog = string.IsNullOrWhiteSpace(databaseName) ? "master" : databaseName.Trim(),
            UserID = userName,
            Password = password,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        }.ConnectionString;

    public static async Task<SqlConnection> OpenWithRetryAsync(
        ApplicationResourceDefinition definition,
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
            $"SQL Server resource '{definition.Name}' started, but CloudShell could not connect to the instance within {ConnectionRetryTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
            lastError);
    }

    private static string? GetAdministratorPassword(ApplicationResourceDefinition application) =>
        application.EnvironmentVariables.FirstOrDefault(variable =>
            string.Equals(variable.Name, "MSSQL_SA_PASSWORD", StringComparison.OrdinalIgnoreCase))?.Value;

    private static string CreateDataSource(Uri endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Host))
        {
            return endpoint.ToString();
        }

        return endpoint.Port > 0
            ? $"{endpoint.Host},{endpoint.Port.ToString(CultureInfo.InvariantCulture)}"
            : endpoint.Host;
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
