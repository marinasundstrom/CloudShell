using CloudShell.Client.Authentication;
using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CloudShell.RabbitMQ.Client;

/// <summary>
/// HTTP resolver for CloudShell-managed RabbitMQ credential material.
/// </summary>
/// <remarks>
/// Experimental API. The endpoint is provider-owned and must only return
/// credential material after validating the caller's CloudShell resource
/// identity and effective RabbitMQ access grants.
/// </remarks>
public sealed class CloudShellRabbitMQCredentialResolver : ICloudShellRabbitMQCredentialResolver
{
    public const string CredentialEndpointEnvironmentVariable = "CLOUDSHELL_RABBITMQ_CREDENTIAL_ENDPOINT";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly CloudShellResourceCredential credential;
    private readonly IReadOnlyList<string>? scopes;

    public CloudShellRabbitMQCredentialResolver(
        Uri credentialEndpoint,
        CloudShellResourceCredential credential,
        IEnumerable<string>? scopes = null)
        : this(credentialEndpoint, credential, new HttpClient(), scopes)
    {
    }

    public CloudShellRabbitMQCredentialResolver(
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
        this.scopes = scopes?.ToArray();
    }

    public Uri CredentialEndpoint { get; }

    public static CloudShellRabbitMQCredentialResolver FromEnvironment(
        CloudShellResourceCredential? credential = null,
        string? rabbitMQResourceName = null,
        IEnumerable<string>? scopes = null) =>
        TryCreateFromEnvironment(credential, rabbitMQResourceName, scopes) ??
        throw new CloudShellCredentialUnavailableException(
            "No CloudShell RabbitMQ credential endpoint was found in the environment.");

    public static CloudShellRabbitMQCredentialResolver? TryCreateFromEnvironment(
        CloudShellResourceCredential? credential = null,
        string? rabbitMQResourceName = null,
        IEnumerable<string>? scopes = null)
    {
        var endpoint = FindEndpoint(rabbitMQResourceName);
        return endpoint is null
            ? null
            : new CloudShellRabbitMQCredentialResolver(
                endpoint,
                credential ?? new DefaultCloudShellResourceCredential(),
                scopes);
    }

    public async ValueTask<CloudShellRabbitMQCredential> ResolveCredentialAsync(
        CloudShellRabbitMQCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = await CreateRequestAsync(request, cancellationToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var credentialResponse = await response.Content.ReadFromJsonAsync<CredentialResponse>(
            SerializerOptions,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(credentialResponse?.Username) ||
            string.IsNullOrWhiteSpace(credentialResponse.Password) ||
            string.IsNullOrWhiteSpace(credentialResponse.VirtualHost))
        {
            throw new CloudShellRabbitMQCredentialException(
                "CloudShell RabbitMQ credential endpoint returned incomplete credentials.");
        }

        return new CloudShellRabbitMQCredential(
            credentialResponse.Username,
            credentialResponse.Password,
            credentialResponse.VirtualHost,
            credentialResponse.ExpiresOn);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        CloudShellRabbitMQCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var permission = NormalizePermission(request.Permission);
        var token = await credential.GetTokenAsync(
            new CloudShellResourceTokenRequest(GetScopes(permission)),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(token.Token))
        {
            throw new CloudShellRabbitMQCredentialException(
                "CloudShell resource credential returned no access token.");
        }

        var message = new HttpRequestMessage(HttpMethod.Post, CredentialEndpoint)
        {
            Content = JsonContent.Create(
                new CredentialRequest(
                    request.RabbitMQResourceName,
                    permission),
                options: SerializerOptions)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return message;
    }

    private IReadOnlyList<string> GetScopes(string permission) =>
        scopes is { Count: > 0 }
            ? scopes
            : [permission];

    private static string NormalizePermission(string? permission) =>
        string.IsNullOrWhiteSpace(permission)
            ? CloudShellRabbitMQPermissions.Configure
            : permission.Trim();

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
                ? $"CloudShell RabbitMQ credential endpoint returned {(int)response.StatusCode}."
                : $"CloudShell RabbitMQ credential endpoint returned {(int)response.StatusCode}. {detail}",
            null,
            response.StatusCode);
    }

    private static Uri? FindEndpoint(string? rabbitMQResourceName)
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
                item.Key.StartsWith("CLOUDSHELL_RABBITMQ_", StringComparison.OrdinalIgnoreCase) &&
                item.Key.EndsWith("_CREDENTIAL_ENDPOINT", StringComparison.OrdinalIgnoreCase) &&
                MatchesRabbitMQResourceName(item.Key, rabbitMQResourceName))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static bool MatchesRabbitMQResourceName(
        string environmentVariableName,
        string? rabbitMQResourceName)
    {
        if (string.IsNullOrWhiteSpace(rabbitMQResourceName))
        {
            return true;
        }

        var normalized = NormalizeEnvironmentSegment(rabbitMQResourceName);
        return environmentVariableName.Contains(
            $"CLOUDSHELL_RABBITMQ_{normalized}_",
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
        string RabbitMQResourceName,
        string Permission);

    private sealed record CredentialResponse(
        string Username,
        string Password,
        string VirtualHost,
        DateTimeOffset? ExpiresOn = null);
}
