namespace CloudShell.Providers.Docker;

public sealed record DockerHostDefinition(
    DockerHostKind Kind,
    Uri Endpoint,
    DockerHostCredentials? Credentials = null)
{
    public static DockerHostDefinition Local(Uri endpoint) =>
        new(DockerHostKind.Local, endpoint);

    public static DockerHostDefinition Remote(
        Uri endpoint,
        DockerHostCredentials? credentials = null) =>
        new(DockerHostKind.Remote, endpoint, credentials);

    public string NormalizedEndpoint => NormalizeEndpoint(Endpoint);

    public string HostIdentity => $"{Kind.ToString().ToLowerInvariant()}:{NormalizedEndpoint}";

    public static string NormalizeEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (endpoint.Scheme.Equals("unix", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Scheme.Equals("npipe", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint.ToString().TrimEnd('/');
        }

        var builder = new UriBuilder(endpoint)
        {
            Scheme = endpoint.Scheme.ToLowerInvariant(),
            Host = endpoint.Host.ToLowerInvariant(),
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        if (IsDefaultPort(builder.Scheme, builder.Port))
        {
            builder.Port = -1;
        }

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static bool IsDefaultPort(string scheme, int port) =>
        (scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && port == 80) ||
        (scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && port == 443) ||
        (scheme.Equals("tcp", StringComparison.OrdinalIgnoreCase) && port == 2375);
}

public enum DockerHostKind
{
    Local,
    Remote
}

public sealed record DockerHostCredentials(
    DockerHostCredentialKind Kind,
    string? Username = null,
    string? PasswordEnvironmentVariable = null,
    string? ClientCertificatePath = null,
    string? ClientKeyPath = null,
    string? CertificateAuthorityPath = null);

public enum DockerHostCredentialKind
{
    None,
    UsernamePasswordEnvironmentVariable,
    TlsCertificateFiles
}
