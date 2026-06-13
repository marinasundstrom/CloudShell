using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;
using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Text.Json;

namespace CloudShell.Configuration;

internal sealed class CloudShellConfigurationProvider(
    CloudShellConfigurationOptions options) : ConfigurationProvider
{
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
            var configuration = new ConfigurationStoreClient(
                new Uri(service.Endpoint),
                service.Credential,
                client,
                [service.IdentityScope]);
            var entries = configuration
                .GetEntriesAsync()
                .GetAwaiter()
                .GetResult();

            foreach (var entry in entries)
            {
                if (!options.LoadSecretValues && entry.IsSecret)
                {
                    continue;
                }

                Data[entry.Name] = entry.Value;
            }

            ClearMetadata("Detail");
            SetMetadata("Status", "connected");
            SetMetadata("LoadedKeys", string.Join(',', entries.Select(entry => entry.Name)));
            SetMetadata("SecretKeys", string.Join(
                ',',
                entries.Where(entry => entry.IsSecret).Select(entry => entry.Name)));
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

    private static CloudShellConfigurationStoreService? ResolveConfigurationStoreService(
        CloudShellConfigurationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return new CloudShellConfigurationStoreService(
                options.Endpoint,
                options.Credential ?? CreateExplicitCredential(options),
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
                new EnvironmentCloudShellResourceCredential(
                    new EnvironmentCloudShellResourceCredentialOptions
                    {
                        TokenEndpoint = identityTokenEndpoint,
                        ClientId = identityClientId,
                        ClientSecret = identityClientSecret,
                        Scope = identityScope,
                        DefaultScope = options.IdentityScope
                    }),
                identityScope);
        }

        return null;
    }

    private static CloudShellResourceCredential CreateExplicitCredential(
        CloudShellConfigurationOptions options) =>
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

}
