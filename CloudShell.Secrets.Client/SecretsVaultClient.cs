using CloudShell.Client.Authentication;
using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CloudShell.Secrets.Client;

/// <summary>
/// SDK client for a CloudShell Secrets Vault service.
/// </summary>
/// <remarks>
/// Public preview API. The client authenticates with a
/// <see cref="CloudShellResourceCredential"/> and calls the protected Secrets
/// Vault HTTP service for the current resource identity.
/// </remarks>
public sealed class SecretsVaultClient
{
    public const string DefaultScope = "ControlPlane.Access";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly CloudShellResourceCredential credential;
    private readonly IReadOnlyList<string> scopes;

    public SecretsVaultClient(
        Uri secretsEndpoint,
        CloudShellResourceCredential credential,
        IEnumerable<string>? scopes = null)
        : this(secretsEndpoint, credential, new HttpClient(), scopes)
    {
    }

    public SecretsVaultClient(
        Uri secretsEndpoint,
        CloudShellResourceCredential credential,
        HttpClient httpClient,
        IEnumerable<string>? scopes = null)
    {
        ArgumentNullException.ThrowIfNull(secretsEndpoint);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(httpClient);

        SecretsEndpoint = secretsEndpoint;
        this.credential = credential;
        this.httpClient = httpClient;
        this.scopes = (scopes ?? [DefaultScope]).ToArray();
    }

    public Uri SecretsEndpoint { get; }

    public static SecretsVaultClient FromEnvironment(
        CloudShellResourceCredential? credential = null,
        string? vaultName = null,
        IEnumerable<string>? scopes = null) =>
        TryCreateFromEnvironment(credential, vaultName, scopes) ??
        throw new CloudShellCredentialUnavailableException(
            "No CloudShell Secrets Vault endpoint was found in the environment.");

    public static SecretsVaultClient? TryCreateFromEnvironment(
        CloudShellResourceCredential? credential = null,
        string? vaultName = null,
        IEnumerable<string>? scopes = null)
    {
        var endpoint = FindEndpoint(vaultName);
        return endpoint is null
            ? null
            : new SecretsVaultClient(
                endpoint,
                credential ?? new DefaultCloudShellResourceCredential(),
                scopes);
    }

    public async Task<IReadOnlyList<SecretProperties>> GetSecretsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            SecretsEndpoint,
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<SecretProperties>>(
            SerializerOptions,
            cancellationToken) ?? [];
    }

    public async Task<SecretValue?> GetSecretAsync(
        string name,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            BuildSecretEndpoint(name, version),
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SecretValue>(
            SerializerOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<CertificateProperties>> GetCertificatesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            BuildCertificatesEndpoint(),
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<CertificateProperties>>(
            SerializerOptions,
            cancellationToken) ?? [];
    }

    public async Task<CertificateValue?> GetCertificateAsync(
        string name,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            BuildCertificateEndpoint(name, version),
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CertificateValue>(
            SerializerOptions,
            cancellationToken);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(
            new CloudShellResourceTokenRequest(scopes),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(token.Token))
        {
            throw new CloudShellAuthenticationException(
                "CloudShell resource credential returned no access token.");
        }

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }

    private Uri BuildSecretEndpoint(string name, string? version)
    {
        var builder = new UriBuilder(SecretsEndpoint);
        var path = builder.Path.TrimEnd('/');
        builder.Path = $"{path}/{Uri.EscapeDataString(name)}";
        if (!string.IsNullOrWhiteSpace(version))
        {
            builder.Query = $"version={Uri.EscapeDataString(version)}";
        }

        return builder.Uri;
    }

    private Uri BuildCertificatesEndpoint()
    {
        var builder = new UriBuilder(SecretsEndpoint);
        var path = builder.Path.TrimEnd('/');
        builder.Path = path.EndsWith("/secrets", StringComparison.OrdinalIgnoreCase)
            ? $"{path[..^"/secrets".Length]}/certificates"
            : $"{path}/certificates";
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private Uri BuildCertificateEndpoint(string name, string? version)
    {
        var builder = new UriBuilder(BuildCertificatesEndpoint());
        var path = builder.Path.TrimEnd('/');
        builder.Path = $"{path}/{Uri.EscapeDataString(name)}";
        if (!string.IsNullOrWhiteSpace(version))
        {
            builder.Query = $"version={Uri.EscapeDataString(version)}";
        }

        return builder.Uri;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail)
                ? $"CloudShell Secrets Vault returned {(int)response.StatusCode}."
                : $"CloudShell Secrets Vault returned {(int)response.StatusCode}. {detail}",
            null,
            response.StatusCode);
    }

    private static Uri? FindEndpoint(string? vaultName)
    {
        var variables = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string && entry.Value is string)
            .ToDictionary(
                entry => (string)entry.Key,
                entry => (string)entry.Value!,
                StringComparer.OrdinalIgnoreCase);

        foreach (var (_, endpoint) in variables
            .Where(item =>
                item.Key.StartsWith("CLOUDSHELL_SECRETS_", StringComparison.OrdinalIgnoreCase) &&
                item.Key.EndsWith("_ENDPOINT", StringComparison.OrdinalIgnoreCase) &&
                MatchesVaultName(item.Key, vaultName))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static bool MatchesVaultName(string environmentVariableName, string? vaultName)
    {
        if (string.IsNullOrWhiteSpace(vaultName))
        {
            return true;
        }

        var normalized = NormalizeEnvironmentSegment(vaultName);
        return environmentVariableName.Contains(
            $"CLOUDSHELL_SECRETS_{normalized}_",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEnvironmentSegment(string value)
    {
        var characters = value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_')
            .ToArray();

        return new string(characters).Trim('_');
    }
}

public sealed record SecretProperties(
    string Name,
    string? Version);

public sealed record SecretValue(
    string Name,
    string Value,
    string? Version);

public sealed record CertificateProperties(
    string Name,
    string? Version,
    string? ContentType,
    string? Thumbprint,
    string? Subject,
    DateTimeOffset? NotBefore,
    DateTimeOffset? Expires,
    bool? HasPrivateKey);

public sealed record CertificateValue(
    string Name,
    string Value,
    string? Version,
    string? ContentType,
    string? Thumbprint,
    string? Subject,
    DateTimeOffset? NotBefore,
    DateTimeOffset? Expires,
    bool? HasPrivateKey);
