using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Net.Http.Headers;
using System.Text.Json;

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
            SetMetadata("Detail", "No CloudShell configuration store service endpoint/token pair was configured or injected.");
            return;
        }

        SetMetadata("Source", service.Endpoint);

        try
        {
            using var client = new HttpClient
            {
                Timeout = options.Timeout
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, service.Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", service.Token);

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
        if (!string.IsNullOrWhiteSpace(options.Endpoint) &&
            !string.IsNullOrWhiteSpace(options.Token))
        {
            return new CloudShellConfigurationStoreService(options.Endpoint, options.Token);
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
            var tokenName = $"{name[..^"_ENDPOINT".Length]}_TOKEN";
            if (string.IsNullOrWhiteSpace(endpoint) ||
                !variables.TryGetValue(tokenName, out var token) ||
                string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            return new CloudShellConfigurationStoreService(endpoint, token);
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
