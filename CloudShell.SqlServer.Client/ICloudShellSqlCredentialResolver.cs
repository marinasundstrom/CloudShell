namespace CloudShell.SqlServer.Client;

/// <summary>
/// Resolves SQL-native credential material for a CloudShell resource identity.
/// </summary>
/// <remarks>
/// Implementations authenticate the current workload to CloudShell, validate
/// effective grants, and ask the SQL provider for credential material that
/// the target SQL Server can understand.
/// </remarks>
public interface ICloudShellSqlCredentialResolver
{
    ValueTask<CloudShellSqlCredential> ResolveCredentialAsync(
        CloudShellSqlConnectionRequest request,
        CancellationToken cancellationToken = default);
}
