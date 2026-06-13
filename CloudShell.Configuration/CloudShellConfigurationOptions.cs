using CloudShell.Abstractions.Authentication;

namespace CloudShell.Configuration;

public sealed class CloudShellConfigurationOptions
{
    public string? Endpoint { get; set; }

    public CloudShellResourceCredential? Credential { get; set; }

    public string? IdentityTokenEndpoint { get; set; }

    public string? IdentityClientId { get; set; }

    public string? IdentityClientSecret { get; set; }

    public string IdentityScope { get; set; } = "ControlPlane.Access";

    public string? ServiceName { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public string MetadataPrefix { get; set; } = "CloudShell:Configuration";

    public bool LoadSecretValues { get; set; } = true;
}
