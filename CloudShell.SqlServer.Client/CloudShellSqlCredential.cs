namespace CloudShell.SqlServer.Client;

/// <summary>
/// Provider-owned SQL credential material resolved for a CloudShell principal.
/// </summary>
/// <remarks>
/// The connection string may contain short-lived SQL-native secrets and must
/// not be logged, stored in resource metadata, or surfaced in diagnostics.
/// </remarks>
public sealed record CloudShellSqlCredential(
    string ConnectionString,
    DateTimeOffset? ExpiresOn = null);
