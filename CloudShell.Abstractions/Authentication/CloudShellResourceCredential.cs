using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.Abstractions.Authentication;

/// <summary>
/// Acquires authentication evidence for a CloudShell resource identity.
/// </summary>
/// <remarks>
/// Public preview API: the platform owns this integration contract, but the
/// credential chain may evolve before the MVP API is declared stable.
/// </remarks>
public abstract class CloudShellResourceCredential
{
    public abstract ValueTask<CloudShellResourceAccessToken> GetTokenAsync(
        CloudShellResourceTokenRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default credential chain for CloudShell resource identities.
/// </summary>
/// <remarks>
/// Public preview API. The first source is environment-provided CloudShell
/// identity client credentials; future sources can add managed identity,
/// federated workload identity, local development credentials, or provider
/// plugins without changing resource consumers.
/// </remarks>
public sealed class DefaultCloudShellResourceCredential : CloudShellResourceCredential
{
    private readonly IReadOnlyList<CloudShellResourceCredential> credentials;

    public DefaultCloudShellResourceCredential()
        : this([new EnvironmentCloudShellResourceCredential()])
    {
    }

    public DefaultCloudShellResourceCredential(
        IEnumerable<CloudShellResourceCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        this.credentials = credentials.ToArray();
    }

    public override async ValueTask<CloudShellResourceAccessToken> GetTokenAsync(
        CloudShellResourceTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unavailableMessages = new List<string>();
        foreach (var credential in credentials)
        {
            try
            {
                return await credential.GetTokenAsync(request, cancellationToken);
            }
            catch (CloudShellCredentialUnavailableException exception)
            {
                unavailableMessages.Add(exception.Message);
            }
        }

        throw new CloudShellCredentialUnavailableException(
            unavailableMessages.Count == 0
                ? "No CloudShell resource credential sources are configured."
                : string.Join(" ", unavailableMessages));
    }
}

/// <summary>
/// Acquires a CloudShell resource identity token from environment-provided
/// client-credentials settings.
/// </summary>
/// <remarks>
/// Public preview API. Reads the same environment contract injected by
/// CloudShell providers for resource workloads:
/// <c>CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT</c>,
/// <c>CLOUDSHELL_IDENTITY_CLIENT_ID</c>,
/// <c>CLOUDSHELL_IDENTITY_CLIENT_SECRET</c>, and
/// <c>CLOUDSHELL_IDENTITY_SCOPE</c>.
/// </remarks>
public sealed class EnvironmentCloudShellResourceCredential : CloudShellResourceCredential
{
    public const string TokenEndpointEnvironmentVariable = "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT";
    public const string ClientIdEnvironmentVariable = "CLOUDSHELL_IDENTITY_CLIENT_ID";
    public const string ClientSecretEnvironmentVariable = "CLOUDSHELL_IDENTITY_CLIENT_SECRET";
    public const string ScopeEnvironmentVariable = "CLOUDSHELL_IDENTITY_SCOPE";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly EnvironmentCloudShellResourceCredentialOptions options;
    private readonly HttpClient httpClient;

    public EnvironmentCloudShellResourceCredential()
        : this(new EnvironmentCloudShellResourceCredentialOptions())
    {
    }

    public EnvironmentCloudShellResourceCredential(
        EnvironmentCloudShellResourceCredentialOptions options)
        : this(options, new HttpClient())
    {
    }

    public EnvironmentCloudShellResourceCredential(
        EnvironmentCloudShellResourceCredentialOptions options,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);

        this.options = options;
        this.httpClient = httpClient;
    }

    public override async ValueTask<CloudShellResourceAccessToken> GetTokenAsync(
        CloudShellResourceTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tokenEndpoint = GetOptionOrEnvironment(
            options.TokenEndpoint,
            TokenEndpointEnvironmentVariable);
        var clientId = GetOptionOrEnvironment(
            options.ClientId,
            ClientIdEnvironmentVariable);
        var clientSecret = GetOptionOrEnvironment(
            options.ClientSecret,
            ClientSecretEnvironmentVariable);
        var scope = GetScope(request);

        if (string.IsNullOrWhiteSpace(tokenEndpoint) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new CloudShellCredentialUnavailableException(
                "CloudShell environment resource credential is unavailable because token endpoint, client id, or client secret is not configured.");
        }

        using var response = await httpClient.PostAsync(
            tokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = scope
            }),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new CloudShellAuthenticationException(
                $"CloudShell identity token endpoint returned {(int)response.StatusCode}." +
                (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {body}"));
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(
            SerializerOptions,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new CloudShellAuthenticationException(
                "CloudShell identity token endpoint returned no access token.");
        }

        return new CloudShellResourceAccessToken(
            token.AccessToken,
            token.ExpiresIn is null
                ? null
                : DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.Value));
    }

    private string GetScope(CloudShellResourceTokenRequest request)
    {
        if (request.Scopes.Count > 0)
        {
            return string.Join(' ', request.Scopes);
        }

        return GetOptionOrEnvironment(options.Scope, ScopeEnvironmentVariable) ??
            options.DefaultScope;
    }

    private static string? GetOptionOrEnvironment(
        string? value,
        string environmentVariable) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : Environment.GetEnvironmentVariable(environmentVariable);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn = null);
}

public sealed class EnvironmentCloudShellResourceCredentialOptions
{
    public string? TokenEndpoint { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string? Scope { get; set; }

    public string DefaultScope { get; set; } = "ControlPlane.Access";
}

public sealed record CloudShellResourceTokenRequest(
    IReadOnlyList<string>? RequestedScopes = null)
{
    public IReadOnlyList<string> Scopes => RequestedScopes ?? [];
}

public sealed record CloudShellResourceAccessToken(
    string Token,
    DateTimeOffset? ExpiresOn = null);

public class CloudShellCredentialUnavailableException(string message) : InvalidOperationException(message);

public class CloudShellAuthenticationException(string message) : InvalidOperationException(message);
