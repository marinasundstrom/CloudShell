using CloudShell.Client.Authentication;

namespace CloudShell.Secrets.Client;

/// <summary>
/// Options for the CloudShell Secrets Vault
/// <see cref="Microsoft.Extensions.Configuration.IConfigurationBuilder"/>
/// integration.
/// </summary>
/// <remarks>
/// Public preview API. Option names, secret-name mapping, and discovery
/// behavior may evolve before the MVP SDK contract is declared stable.
/// </remarks>
public sealed class CloudShellSecretsVaultOptions
{
    public string? Endpoint { get; set; }

    public CloudShellResourceCredential? Credential { get; set; }

    public string? IdentityTokenEndpoint { get; set; }

    public string? IdentityClientId { get; set; }

    public string? IdentityClientSecret { get; set; }

    public string IdentityScope { get; set; } = SecretsVaultClient.DefaultScope;

    public string? VaultName { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public string MetadataPrefix { get; set; } = "CloudShell:SecretsVault";

    public string KeyDelimiterReplacement { get; set; } = "--";
}
