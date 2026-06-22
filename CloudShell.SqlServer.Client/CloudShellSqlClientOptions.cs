using CloudShell.Client.Authentication;

namespace CloudShell.SqlServer.Client;

/// <summary>
/// Options for CloudShell-managed SQL Server credential resolution.
/// </summary>
public sealed class CloudShellSqlClientOptions
{
    public Uri? CredentialEndpoint { get; set; }

    public string? SqlServerResourceName { get; set; }

    public CloudShellResourceCredential? Credential { get; set; }

    public IReadOnlyList<string>? Scopes { get; set; }
}
