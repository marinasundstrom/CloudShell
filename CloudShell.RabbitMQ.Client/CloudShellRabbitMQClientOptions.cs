using CloudShell.Client.Authentication;

namespace CloudShell.RabbitMQ.Client;

/// <summary>
/// Options for CloudShell-managed RabbitMQ credential resolution.
/// </summary>
public sealed class CloudShellRabbitMQClientOptions
{
    public Uri? CredentialEndpoint { get; set; }

    public string? RabbitMQResourceName { get; set; }

    public string? Permission { get; set; }

    public string? HostName { get; set; }

    public int? Port { get; set; }

    public string? ClientProvidedName { get; set; }

    public CloudShellResourceCredential? Credential { get; set; }

    public IReadOnlyList<string>? Scopes { get; set; }
}
