namespace CloudShell.SqlServer.Client;

/// <summary>
/// Request to resolve SQL Server access for the current CloudShell principal.
/// </summary>
/// <param name="SqlServerResourceName">
/// The CloudShell SQL Server resource name or stable resource identifier.
/// </param>
/// <param name="DatabaseName">The database to connect to.</param>
/// <param name="Permission">
/// Optional data-plane permission requested by the workload. When omitted,
/// the provider uses its default connect/read-write policy for the target.
/// </param>
public sealed record CloudShellSqlConnectionRequest(
    string SqlServerResourceName,
    string DatabaseName,
    string? Permission = null)
{
    public string SqlServerResourceName { get; init; } =
        string.IsNullOrWhiteSpace(SqlServerResourceName)
            ? throw new ArgumentException(
                "SQL Server resource name is required.",
                nameof(SqlServerResourceName))
            : SqlServerResourceName;

    public string DatabaseName { get; init; } =
        string.IsNullOrWhiteSpace(DatabaseName)
            ? throw new ArgumentException(
                "Database name is required.",
                nameof(DatabaseName))
            : DatabaseName;
}
