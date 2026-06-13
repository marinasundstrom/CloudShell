using CloudShell.Client.Authentication;

namespace CloudShell.Configuration.Client;

/// <summary>
/// Options for the CloudShell Configuration Store
/// <see cref="Microsoft.Extensions.Configuration.IConfigurationBuilder"/>
/// integration.
/// </summary>
/// <remarks>
/// Public preview API. Option names and discovery behavior may evolve before
/// the MVP SDK contract is declared stable.
/// </remarks>
public sealed class CloudShellConfigurationStoreOptions
{
    public string? Endpoint { get; set; }

    public CloudShellResourceCredential? Credential { get; set; }

    public string? IdentityTokenEndpoint { get; set; }

    public string? IdentityClientId { get; set; }

    public string? IdentityClientSecret { get; set; }

    public string IdentityScope { get; set; } = ConfigurationStoreClient.DefaultScope;

    public string? ServiceName { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public string MetadataPrefix { get; set; } = "CloudShell:ConfigurationStore";

    public bool LoadSecretValues { get; set; } = true;
}
