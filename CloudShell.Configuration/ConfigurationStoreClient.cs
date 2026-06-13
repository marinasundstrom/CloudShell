using CloudShell.Abstractions.Authentication;
using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CloudShell.Configuration;

/// <summary>
/// SDK client for a CloudShell Configuration Store service.
/// </summary>
/// <remarks>
/// Public preview API. The client authenticates with a
/// <see cref="CloudShellResourceCredential"/> and calls the protected
/// Configuration Store HTTP service for the current resource identity.
/// </remarks>
public sealed class ConfigurationStoreClient
{
    public const string DefaultScope = "ControlPlane.Access";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly CloudShellResourceCredential credential;
    private readonly IReadOnlyList<string> scopes;

    public ConfigurationStoreClient(
        Uri entriesEndpoint,
        CloudShellResourceCredential credential,
        IEnumerable<string>? scopes = null)
        : this(entriesEndpoint, credential, new HttpClient(), scopes)
    {
    }

    public ConfigurationStoreClient(
        Uri entriesEndpoint,
        CloudShellResourceCredential credential,
        HttpClient httpClient,
        IEnumerable<string>? scopes = null)
    {
        ArgumentNullException.ThrowIfNull(entriesEndpoint);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(httpClient);

        EntriesEndpoint = entriesEndpoint;
        this.credential = credential;
        this.httpClient = httpClient;
        this.scopes = (scopes ?? [DefaultScope]).ToArray();
    }

    public Uri EntriesEndpoint { get; }

    public static ConfigurationStoreClient FromEnvironment(
        CloudShellResourceCredential? credential = null,
        string? serviceName = null,
        IEnumerable<string>? scopes = null) =>
        TryCreateFromEnvironment(credential, serviceName, scopes) ??
        throw new CloudShellCredentialUnavailableException(
            "No CloudShell configuration store endpoint was found in the environment.");

    public static ConfigurationStoreClient? TryCreateFromEnvironment(
        CloudShellResourceCredential? credential = null,
        string? serviceName = null,
        IEnumerable<string>? scopes = null)
    {
        var endpoint = FindEndpoint(serviceName);
        return endpoint is null
            ? null
            : new ConfigurationStoreClient(
                endpoint,
                credential ?? new DefaultCloudShellResourceCredential(),
                scopes);
    }

    public async Task<IReadOnlyList<CloudShellConfigurationEntry>> GetEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            EntriesEndpoint,
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<CloudShellConfigurationEntry>>(
            SerializerOptions,
            cancellationToken) ?? [];
    }

    public async Task<CloudShellConfigurationEntry?> GetEntryAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            BuildEntryEndpoint(name),
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CloudShellConfigurationEntry>(
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

    private Uri BuildEntryEndpoint(string name)
    {
        var builder = new UriBuilder(EntriesEndpoint);
        var path = builder.Path.TrimEnd('/');
        builder.Path = $"{path}/{Uri.EscapeDataString(name)}";
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
                ? $"CloudShell Configuration Store returned {(int)response.StatusCode}."
                : $"CloudShell Configuration Store returned {(int)response.StatusCode}. {detail}",
            null,
            response.StatusCode);
    }

    private static Uri? FindEndpoint(string? serviceName)
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
                item.Key.StartsWith("CLOUDSHELL_CONFIGURATION_", StringComparison.OrdinalIgnoreCase) &&
                item.Key.EndsWith("_ENDPOINT", StringComparison.OrdinalIgnoreCase) &&
                MatchesServiceName(item.Key, serviceName))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static bool MatchesServiceName(string environmentVariableName, string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return true;
        }

        var normalized = NormalizeEnvironmentSegment(serviceName);
        return environmentVariableName.Contains(
            $"CLOUDSHELL_CONFIGURATION_{normalized}_",
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
