using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.Configuration;

internal sealed class CloudShellConfigurationProvider(
    CloudShellConfigurationOptions options) : ConfigurationProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public override void Load()
    {
        var service = ResolveConfigurationStoreService(options);
        if (service is null)
        {
            SetMetadata("Status", "unavailable");
            SetMetadata("Detail", "No CloudShell configuration store service endpoint and identity credential were configured or injected.");
            return;
        }

        SetMetadata("Source", service.Endpoint);

        try
        {
            using var client = new HttpClient
            {
                Timeout = options.Timeout
            };
            var token = ResolveAccessToken(client, service);
            if (string.IsNullOrWhiteSpace(token))
            {
                SetMetadata("Status", "unavailable");
                SetMetadata("Detail", "No CloudShell configuration access token could be acquired.");
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, service.Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                SetMetadata("Status", "unavailable");
                SetMetadata("Detail", $"CloudShell configuration store service returned {(int)response.StatusCode}.");
                return;
            }

            using var stream = response.Content.ReadAsStream();
            var entries = JsonSerializer.Deserialize<IReadOnlyList<CloudShellConfigurationEntry>>(
                stream,
                SerializerOptions) ?? [];

            foreach (var entry in entries)
            {
                if (!options.LoadSecretValues && entry.IsSecret)
                {
                    continue;
                }

                Data[entry.Name] = entry.Value;
            }

            SetMetadata("Status", "connected");
            SetMetadata("LoadedKeys", string.Join(',', entries.Select(entry => entry.Name)));
            SetMetadata("SecretKeys", string.Join(
                ',',
                entries.Where(entry => entry.IsSecret).Select(entry => entry.Name)));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            SetMetadata("Status", "unavailable");
            SetMetadata("Detail", exception.Message);
        }
    }

    private void SetMetadata(string name, string value) =>
        Data[$"{options.MetadataPrefix}:{name}"] = value;

    private static CloudShellConfigurationStoreService? ResolveConfigurationStoreService(
        CloudShellConfigurationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return new CloudShellConfigurationStoreService(
                options.Endpoint,
                options.IdentityTokenEndpoint,
                options.IdentityClientId,
                options.IdentityClientSecret,
                options.IdentityScope);
        }

        var variables = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string && entry.Value is string)
            .ToDictionary(
                entry => (string)entry.Key,
                entry => (string)entry.Value!,
                StringComparer.OrdinalIgnoreCase);

        foreach (var (name, endpoint) in variables
            .Where(item =>
                item.Key.StartsWith("CLOUDSHELL_CONFIGURATION_", StringComparison.OrdinalIgnoreCase) &&
                item.Key.EndsWith("_ENDPOINT", StringComparison.OrdinalIgnoreCase) &&
                MatchesServiceName(item.Key, options.ServiceName))
            .OrderByDescending(item => MatchesServiceName(item.Key, "EXAMPLE_CONFIGURATION"))
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var identityTokenEndpoint = GetOptionalVariable(variables, "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT");
            var identityClientId = GetOptionalVariable(variables, "CLOUDSHELL_IDENTITY_CLIENT_ID");
            var identityClientSecret = GetOptionalVariable(variables, "CLOUDSHELL_IDENTITY_CLIENT_SECRET");
            var identityScope = GetOptionalVariable(variables, "CLOUDSHELL_IDENTITY_SCOPE") ??
                options.IdentityScope;
            if (string.IsNullOrWhiteSpace(endpoint) ||
                (string.IsNullOrWhiteSpace(identityTokenEndpoint) ||
                 string.IsNullOrWhiteSpace(identityClientId) ||
                 string.IsNullOrWhiteSpace(identityClientSecret)))
            {
                continue;
            }

            return new CloudShellConfigurationStoreService(
                endpoint,
                identityTokenEndpoint,
                identityClientId,
                identityClientSecret,
                identityScope);
        }

        return null;
    }

    private static string? ResolveAccessToken(
        HttpClient client,
        CloudShellConfigurationStoreService service)
    {
        if (!string.IsNullOrWhiteSpace(service.IdentityTokenEndpoint) &&
            !string.IsNullOrWhiteSpace(service.IdentityClientId) &&
            !string.IsNullOrWhiteSpace(service.IdentityClientSecret))
        {
            using var response = client.PostAsync(
                service.IdentityTokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = service.IdentityClientId,
                    ["client_secret"] = service.IdentityClientSecret,
                    ["scope"] = service.IdentityScope
                })).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var token = response.Content
                .ReadFromJsonAsync<TokenResponse>(SerializerOptions)
                .GetAwaiter()
                .GetResult();
            return token?.AccessToken;
        }
        return null;
    }

    private static string? GetOptionalVariable(
        IReadOnlyDictionary<string, string> variables,
        string name) =>
        variables.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

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

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken);
}
