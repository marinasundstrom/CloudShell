using CloudShell.Client.Authentication;
using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Text.Json;

namespace CloudShell.Configuration.Client;

internal sealed class CloudShellConfigurationStoreProvider(
    CloudShellConfigurationStoreOptions options) : ConfigurationProvider
{
    public override void Load()
    {
        var endpoint = ResolveEndpoint(options);
        if (endpoint is null)
        {
            SetMetadata("Status", "unavailable");
            SetMetadata("Detail", "No CloudShell Configuration Store endpoint was configured or injected.");
            return;
        }

        SetMetadata("Source", endpoint.ToString());

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = options.Timeout
            };
            var client = new ConfigurationStoreClient(
                endpoint,
                options.Credential ?? CreateExplicitCredential(options),
                httpClient,
                [options.IdentityScope]);
            var settings = client
                .GetSettingsAsync()
                .GetAwaiter()
                .GetResult();

            foreach (var setting in settings)
            {
                Data[ToConfigurationKey(setting.Name)] = setting.Value;
            }

            ClearMetadata("Detail");
            SetMetadata("Status", "connected");
            SetMetadata("LoadedKeys", string.Join(',', settings.Select(setting => ToConfigurationKey(setting.Name))));
        }
        catch (Exception exception) when (
            exception is CloudShellCredentialUnavailableException or
                CloudShellAuthenticationException or
                HttpRequestException or
                TaskCanceledException or
                JsonException)
        {
            SetMetadata("Status", "unavailable");
            SetMetadata("Detail", exception.Message);
        }
    }

    private void SetMetadata(string name, string value) =>
        Data[$"{options.MetadataPrefix}:{name}"] = value;

    private void ClearMetadata(string name) =>
        Data.Remove($"{options.MetadataPrefix}:{name}");

    private string ToConfigurationKey(string settingName) =>
        string.IsNullOrEmpty(options.KeyDelimiterReplacement)
            ? settingName
            : settingName.Replace(
                options.KeyDelimiterReplacement,
                ConfigurationPath.KeyDelimiter,
                StringComparison.Ordinal);

    private static Uri? ResolveEndpoint(CloudShellConfigurationStoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Endpoint) &&
            Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var configuredEndpoint))
        {
            return configuredEndpoint;
        }

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
                MatchesServiceName(item.Key, options.ServiceName))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return uri;
            }
        }

        return null;
    }

    private static CloudShellResourceCredential CreateExplicitCredential(
        CloudShellConfigurationStoreOptions options) =>
        string.IsNullOrWhiteSpace(options.IdentityTokenEndpoint) &&
        string.IsNullOrWhiteSpace(options.IdentityClientId) &&
        string.IsNullOrWhiteSpace(options.IdentityClientSecret)
            ? new DefaultCloudShellResourceCredential()
            : new EnvironmentCloudShellResourceCredential(
                new EnvironmentCloudShellResourceCredentialOptions
                {
                    TokenEndpoint = options.IdentityTokenEndpoint,
                    ClientId = options.IdentityClientId,
                    ClientSecret = options.IdentityClientSecret,
                    Scope = options.IdentityScope,
                    DefaultScope = options.IdentityScope
                });

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
