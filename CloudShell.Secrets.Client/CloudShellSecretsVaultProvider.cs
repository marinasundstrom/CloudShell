using CloudShell.Client.Authentication;
using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Text.Json;

namespace CloudShell.Secrets.Client;

internal sealed class CloudShellSecretsVaultProvider(
    CloudShellSecretsVaultOptions options) : ConfigurationProvider
{
    public override void Load()
    {
        var endpoint = ResolveEndpoint(options);
        if (endpoint is null)
        {
            SetMetadata("Status", "unavailable");
            SetMetadata("Detail", "No CloudShell Secrets Vault endpoint was configured or injected.");
            return;
        }

        SetMetadata("Source", endpoint.ToString());

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = options.Timeout
            };
            var client = new SecretsVaultClient(
                endpoint,
                options.Credential ?? CreateExplicitCredential(options),
                httpClient,
                [options.IdentityScope]);
            var secrets = client
                .GetSecretsAsync()
                .GetAwaiter()
                .GetResult();
            var loadedKeys = new List<string>();

            foreach (var secretProperties in secrets)
            {
                var secret = client
                    .GetSecretAsync(secretProperties.Name, secretProperties.Version)
                    .GetAwaiter()
                    .GetResult();
                if (secret is null)
                {
                    continue;
                }

                var key = ToConfigurationKey(secret.Name);
                Data[key] = secret.Value;
                loadedKeys.Add(key);
            }

            ClearMetadata("Detail");
            SetMetadata("Status", "connected");
            SetMetadata("LoadedKeys", string.Join(',', loadedKeys));
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

    private string ToConfigurationKey(string secretName) =>
        string.IsNullOrEmpty(options.KeyDelimiterReplacement)
            ? secretName
            : secretName.Replace(
                options.KeyDelimiterReplacement,
                ConfigurationPath.KeyDelimiter,
                StringComparison.Ordinal);

    private void SetMetadata(string name, string value) =>
        Data[$"{options.MetadataPrefix}:{name}"] = value;

    private void ClearMetadata(string name) =>
        Data.Remove($"{options.MetadataPrefix}:{name}");

    private static Uri? ResolveEndpoint(CloudShellSecretsVaultOptions options)
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
                item.Key.StartsWith("CLOUDSHELL_SECRETS_", StringComparison.OrdinalIgnoreCase) &&
                item.Key.EndsWith("_ENDPOINT", StringComparison.OrdinalIgnoreCase) &&
                MatchesVaultName(item.Key, options.VaultName))
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
        CloudShellSecretsVaultOptions options) =>
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
