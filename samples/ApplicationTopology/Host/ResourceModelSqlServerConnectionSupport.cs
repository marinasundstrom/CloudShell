using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;

namespace CloudShell.ApplicationTopologyHost;

internal static class ResourceModelSqlServerConnectionSupport
{
    private static readonly TimeSpan ConnectionRetryTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromSeconds(1);

    public static bool TryCreateAdministratorConnectionString(
        Resource server,
        IConfiguration configuration,
        string databaseName,
        out string connectionString)
    {
        connectionString = string.Empty;

        var endpoint = GetTdsEndpoint(server);
        if (endpoint is null ||
            !TryCreateDataSource(endpoint, out var dataSource))
        {
            return false;
        }

        var password = configuration["ApplicationTopology:SqlServer:Password"] ??
            SqlServerResourceDefaults.AdministratorPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        connectionString = CreateConnectionString(
            dataSource,
            string.IsNullOrWhiteSpace(databaseName) ? "master" : databaseName.Trim(),
            "sa",
            password);
        return true;
    }

    public static bool TryCreateCredentialConnectionString(
        Resource server,
        string databaseName,
        string userName,
        string password,
        out string connectionString)
    {
        connectionString = string.Empty;

        var endpoint = GetTdsEndpoint(server);
        if (endpoint is null ||
            !TryCreateDataSource(endpoint, out var dataSource))
        {
            return false;
        }

        connectionString = CreateConnectionString(
            dataSource,
            databaseName,
            userName,
            password);
        return true;
    }

    public static async Task<SqlConnection> OpenWithRetryAsync(
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
            $"SQL Server resource '{server.Name}' did not accept connections within {ConnectionRetryTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
            lastError);
    }

    private static NetworkingEndpointRequestValue? GetTdsEndpoint(Resource server) =>
        server.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                SqlServerResourceTypeProvider.Attributes.EndpointRequests)?
            .FirstOrDefault(endpoint =>
                string.Equals(endpoint.Name, "tds", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(endpoint.Protocol, "tcp", StringComparison.OrdinalIgnoreCase));

    private static bool TryCreateDataSource(
        NetworkingEndpointRequestValue endpoint,
        out string dataSource)
    {
        dataSource = string.Empty;

        var host = endpoint.Host;
        if (string.IsNullOrWhiteSpace(host) ||
            endpoint.Port is not > 0)
        {
            return false;
        }

        dataSource = $"{host.Trim()},{endpoint.Port.Value.ToString(CultureInfo.InvariantCulture)}";
        return true;
    }

    private static string CreateConnectionString(
        string dataSource,
        string databaseName,
        string userName,
        string password) =>
        new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = string.IsNullOrWhiteSpace(databaseName) ? "master" : databaseName.Trim(),
            UserID = userName,
            Password = password,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        }.ConnectionString;

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
