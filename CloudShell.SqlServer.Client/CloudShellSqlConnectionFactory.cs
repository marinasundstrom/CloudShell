using Microsoft.Data.SqlClient;

namespace CloudShell.SqlServer.Client;

/// <summary>
/// Creates SQL Server connections whose credentials are resolved by CloudShell.
/// </summary>
/// <remarks>
/// Experimental API. This keeps ordinary SQL Server connection strings
/// supported while giving workloads a managed-identity-shaped access path.
/// The resolver owns all provider-specific credential exchange.
/// </remarks>
public sealed class CloudShellSqlConnectionFactory
{
    private readonly ICloudShellSqlCredentialResolver resolver;

    public CloudShellSqlConnectionFactory(ICloudShellSqlCredentialResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        this.resolver = resolver;
    }

    public async ValueTask<SqlConnection> CreateConnectionAsync(
        string sqlServerResourceName,
        string databaseName,
        CancellationToken cancellationToken = default) =>
        await CreateConnectionAsync(
            new CloudShellSqlConnectionRequest(sqlServerResourceName, databaseName),
            cancellationToken);

    public async ValueTask<SqlConnection> CreateConnectionAsync(
        CloudShellSqlConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var credential = await resolver.ResolveCredentialAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(credential.ConnectionString))
        {
            throw new CloudShellSqlCredentialException(
                "CloudShell SQL credential resolver returned no connection string.");
        }

        return new SqlConnection(credential.ConnectionString);
    }

    public async ValueTask<SqlConnection> OpenConnectionAsync(
        string sqlServerResourceName,
        string databaseName,
        CancellationToken cancellationToken = default) =>
        await OpenConnectionAsync(
            new CloudShellSqlConnectionRequest(sqlServerResourceName, databaseName),
            cancellationToken);

    public async ValueTask<SqlConnection> OpenConnectionAsync(
        CloudShellSqlConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var connection = await CreateConnectionAsync(request, cancellationToken);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
