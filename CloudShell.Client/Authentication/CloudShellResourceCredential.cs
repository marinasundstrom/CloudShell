using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.Client.Authentication;

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
/// Public preview API. The chain prefers environment-provided CloudShell
/// identity client credentials, then a developer profile credential. Future
/// sources can add managed identity, federated workload identity, or provider
/// plugins without changing resource consumers.
/// </remarks>
public sealed class DefaultCloudShellResourceCredential : CloudShellResourceCredential
{
    private readonly IReadOnlyList<CloudShellResourceCredential> credentials;

    public DefaultCloudShellResourceCredential()
        : this(
            [
                new EnvironmentCloudShellResourceCredential(),
                new CloudShellProfileCredential()
            ])
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
/// Acquires a CloudShell resource identity token from the local CloudShell
/// profile directory.
/// </summary>
/// <remarks>
/// Public preview API. Reads <c>config.json</c> from <c>~/.cloudshell</c> by
/// default, or from <c>CLOUDSHELL_CONFIG_DIR</c> when that environment variable
/// is set. <c>CLOUDSHELL_PROFILE</c> selects a named profile when specified.
/// </remarks>
public sealed class CloudShellProfileCredential : CloudShellResourceCredential
{
    public const string ConfigDirectoryEnvironmentVariable = "CLOUDSHELL_CONFIG_DIR";
    public const string ProfileEnvironmentVariable = "CLOUDSHELL_PROFILE";
    public const string DefaultConfigDirectoryName = ".cloudshell";
    public const string DefaultConfigFileName = "config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly CloudShellProfileCredentialOptions options;

    public CloudShellProfileCredential()
        : this(new CloudShellProfileCredentialOptions())
    {
    }

    public CloudShellProfileCredential(CloudShellProfileCredentialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
    }

    public override async ValueTask<CloudShellResourceAccessToken> GetTokenAsync(
        CloudShellResourceTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configPath = ResolveConfigPath();
        if (!File.Exists(configPath))
        {
            throw new CloudShellCredentialUnavailableException(
                $"CloudShell profile credential is unavailable because '{configPath}' does not exist.");
        }

        CloudShellProfileConfiguration configuration;
        try
        {
            await using var stream = File.OpenRead(configPath);
            configuration = await JsonSerializer.DeserializeAsync<CloudShellProfileConfiguration>(
                stream,
                SerializerOptions,
                cancellationToken) ??
                new CloudShellProfileConfiguration();
        }
        catch (JsonException exception)
        {
            throw new CloudShellAuthenticationException(
                $"CloudShell profile credential could not parse '{configPath}'. {exception.Message}");
        }

        var profileName = GetOptionOrEnvironment(options.ProfileName, ProfileEnvironmentVariable) ??
            configuration.ActiveProfile;
        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new CloudShellCredentialUnavailableException(
                "CloudShell profile credential is unavailable because no active profile is configured.");
        }

        var profile = FindProfile(configuration, profileName);
        if (profile is null)
        {
            throw new CloudShellCredentialUnavailableException(
                $"CloudShell profile credential is unavailable because profile '{profileName}' was not found.");
        }

        var credentialDefinition = profile.Credential;
        if (credentialDefinition is null ||
            !string.Equals(credentialDefinition.Kind, "staticBearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new CloudShellCredentialUnavailableException(
                $"CloudShell profile '{profileName}' does not contain a supported resource credential.");
        }

        if (credentialDefinition.ExpiresOn is { } expiresOn &&
            expiresOn <= DateTimeOffset.UtcNow)
        {
            throw new CloudShellCredentialUnavailableException(
                $"CloudShell profile credential for profile '{profileName}' has expired.");
        }

        var token = await ResolveStaticBearerTokenAsync(
            credentialDefinition,
            Path.GetDirectoryName(configPath) ?? string.Empty,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CloudShellCredentialUnavailableException(
                $"CloudShell profile '{profileName}' does not contain an access token.");
        }

        return new CloudShellResourceAccessToken(
            token.Trim(),
            credentialDefinition.ExpiresOn);
    }

    private string ResolveConfigPath()
    {
        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            return options.ConfigPath;
        }

        return Path.Combine(ResolveConfigDirectory(), DefaultConfigFileName);
    }

    private string ResolveConfigDirectory()
    {
        var configuredDirectory = GetOptionOrEnvironment(
            options.ConfigDirectory,
            ConfigDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return configuredDirectory;
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            throw new CloudShellCredentialUnavailableException(
                "CloudShell profile credential is unavailable because no user profile directory could be resolved.");
        }

        return Path.Combine(homeDirectory, DefaultConfigDirectoryName);
    }

    private static CloudShellProfile? FindProfile(
        CloudShellProfileConfiguration configuration,
        string profileName)
    {
        if (configuration.Profiles.TryGetValue(profileName, out var profile))
        {
            return profile;
        }

        foreach (var candidate in configuration.Profiles)
        {
            if (string.Equals(candidate.Key, profileName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value;
            }
        }

        return null;
    }

    private static async ValueTask<string?> ResolveStaticBearerTokenAsync(
        CloudShellProfileCredentialDefinition credential,
        string configDirectory,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(credential.AccessToken))
        {
            return credential.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(credential.AccessTokenPath))
        {
            return null;
        }

        var tokenPath = Path.IsPathRooted(credential.AccessTokenPath)
            ? credential.AccessTokenPath
            : Path.Combine(configDirectory, credential.AccessTokenPath);
        if (!File.Exists(tokenPath))
        {
            throw new CloudShellCredentialUnavailableException(
                $"CloudShell profile credential is unavailable because token file '{tokenPath}' does not exist.");
        }

        return await File.ReadAllTextAsync(tokenPath, cancellationToken);
    }

    private static string? GetOptionOrEnvironment(
        string? value,
        string environmentVariable) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : Environment.GetEnvironmentVariable(environmentVariable);

    private sealed class CloudShellProfileConfiguration
    {
        public string? ActiveProfile { get; set; }

        public Dictionary<string, CloudShellProfile> Profiles { get; set; } = [];
    }

    private sealed class CloudShellProfile
    {
        public string? ControlPlane { get; set; }

        public string? Environment { get; set; }

        public CloudShellProfileCredentialDefinition? Credential { get; set; }
    }

    private sealed class CloudShellProfileCredentialDefinition
    {
        public string? Kind { get; set; }

        public string? AccessToken { get; set; }

        public string? AccessTokenPath { get; set; }

        public DateTimeOffset? ExpiresOn { get; set; }
    }
}

public sealed class CloudShellProfileCredentialOptions
{
    public string? ConfigDirectory { get; set; }

    public string? ConfigPath { get; set; }

    public string? ProfileName { get; set; }
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
