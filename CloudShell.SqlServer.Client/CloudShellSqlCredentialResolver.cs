using CloudShell.Client.Authentication;
using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CloudShell.SqlServer.Client;

/// <summary>
/// HTTP resolver for CloudShell-managed SQL Server credential material.
/// </summary>
/// <remarks>
/// Experimental API. The endpoint is provider-owned and must only return
/// credential material after validating the caller's CloudShell resource
/// identity and effective SQL access grants.
/// </remarks>
public sealed class CloudShellSqlCredentialResolver : ICloudShellSqlCredentialResolver
{
    public const string DefaultScope = "ControlPlane.Access";
    public const string CredentialEndpointEnvironmentVariable = "CLOUDSHELL_SQL_CREDENTIAL_ENDPOINT";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly CloudShellResourceCredential credential;
    private readonly IReadOnlyList<string> scopes;

    public CloudShellSqlCredentialResolver(
        Uri credentialEndpoint,
        CloudShellResourceCredential credential,
        IEnumerable<string>? scopes = null)
        : this(credentialEndpoint, credential, new HttpClient(), scopes)
    {
    }

    public CloudShellSqlCredentialResolver(
        Uri credentialEndpoint,
        CloudShellResourceCredential credential,
        HttpClient httpClient,
        IEnumerable<string>? scopes = null)
    {
        ArgumentNullException.ThrowIfNull(credentialEndpoint);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(httpClient);

        CredentialEndpoint = credentialEndpoint;
        this.credential = credential;
        this.httpClient = httpClient;
        this.scopes = (scopes ?? [DefaultScope]).ToArray();
    }

    public Uri CredentialEndpoint { get; }

    public static CloudShellSqlCredentialResolver FromEnvironment(
        CloudShellResourceCredential? credential = null,
        string? sqlServerResourceName = null,
        IEnumerable<string>? scopes = null) =>
        TryCreateFromEnvironment(credential, sqlServerResourceName, scopes) ??
        throw new CloudShellCredentialUnavailableException(
            "No CloudShell SQL credential endpoint was found in the environment.");

    public static CloudShellSqlCredentialResolver? TryCreateFromEnvironment(
        CloudShellResourceCredential? credential = null,
        string? sqlServerResourceName = null,
        IEnumerable<string>? scopes = null)
    {
        var endpoint = FindEndpoint(sqlServerResourceName);
        return endpoint is null
            ? null
            : new CloudShellSqlCredentialResolver(
                endpoint,
                credential ?? new DefaultCloudShellResourceCredential(),
                scopes);
    }

    public async ValueTask<CloudShellSqlCredential> ResolveCredentialAsync(
        CloudShellSqlConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = await CreateRequestAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var credentialResponse = await response.Content.ReadFromJsonAsync<CredentialResponse>(
            SerializerOptions,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(credentialResponse?.ConnectionString))
        {
            throw new CloudShellSqlCredentialException(
                "CloudShell SQL credential endpoint returned no connection string.");
        }

        return new CloudShellSqlCredential(
            credentialResponse.ConnectionString,
            credentialResponse.ExpiresOn);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        CloudShellSqlConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(
            new CloudShellResourceTokenRequest(scopes),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(token.Token))
        {
            throw new CloudShellSqlCredentialException(
                "CloudShell resource credential returned no access token.");
        }

        var message = new HttpRequestMessage(HttpMethod.Post, CredentialEndpoint)
        {
            Content = JsonContent.Create(
                new CredentialRequest(
                    request.SqlServerResourceName,
                    request.DatabaseName,
                    request.Permission),
                options: SerializerOptions)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return message;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = response.StatusCode == HttpStatusCode.InternalServerError
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail)
                ? $"CloudShell SQL credential endpoint returned {(int)response.StatusCode}."
                : $"CloudShell SQL credential endpoint returned {(int)response.StatusCode}. {detail}",
            null,
            response.StatusCode);
    }

    private static Uri? FindEndpoint(string? sqlServerResourceName)
    {
        var variables = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string && entry.Value is string)
            .ToDictionary(
                entry => (string)entry.Key,
                entry => (string)entry.Value!,
                StringComparer.OrdinalIgnoreCase);

        if (variables.TryGetValue(CredentialEndpointEnvironmentVariable, out var endpoint) &&
            Uri.TryCreate(endpoint, UriKind.Absolute, out var defaultEndpoint))
        {
            return defaultEndpoint;
        }

        foreach (var (_, candidate) in variables
            .Where(item =>
                item.Key.StartsWith("CLOUDSHELL_SQL_", StringComparison.OrdinalIgnoreCase) &&
                item.Key.EndsWith("_CREDENTIAL_ENDPOINT", StringComparison.OrdinalIgnoreCase) &&
                MatchesSqlServerResourceName(item.Key, sqlServerResourceName))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static bool MatchesSqlServerResourceName(
        string environmentVariableName,
        string? sqlServerResourceName)
    {
        if (string.IsNullOrWhiteSpace(sqlServerResourceName))
        {
            return true;
        }

        var normalized = NormalizeEnvironmentSegment(sqlServerResourceName);
        return environmentVariableName.Contains(
            $"CLOUDSHELL_SQL_{normalized}_",
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

    private sealed record CredentialRequest(
        string SqlServerResourceName,
        string DatabaseName,
        string? Permission);

    private sealed record CredentialResponse(
        string ConnectionString,
        DateTimeOffset? ExpiresOn = null);
}
