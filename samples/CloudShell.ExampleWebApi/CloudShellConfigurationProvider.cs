using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Net.Http.Headers;
using System.Text.Json;

public static class CloudShellConfigurationExtensions
{
    public static IConfigurationBuilder AddCloudShellConfiguration(this IConfigurationBuilder builder)
    {
        builder.Add(new CloudShellConfigurationSource());
        return builder;
    }
}

internal sealed class CloudShellConfigurationSource : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new CloudShellConfigurationProvider();
}

internal sealed class CloudShellConfigurationProvider : ConfigurationProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public override void Load()
    {
        var service = ResolveConfigurationService();
        if (service is null)
        {
            Data["CloudShell:Configuration:Status"] = "unavailable";
            Data["CloudShell:Configuration:Detail"] = "No CloudShell configuration service endpoint/token pair was injected.";
            return;
        }

        Data["CloudShell:Configuration:Source"] = service.Endpoint;

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, service.Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", service.Token);

            using var response = client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                Data["CloudShell:Configuration:Status"] = "unavailable";
                Data["CloudShell:Configuration:Detail"] =
                    $"CloudShell configuration service returned {(int)response.StatusCode}.";
                return;
            }

            using var stream = response.Content.ReadAsStream();
            var entries = JsonSerializer.Deserialize<IReadOnlyList<CloudShellConfigurationEntry>>(
                stream,
                SerializerOptions) ?? [];

            foreach (var entry in entries)
            {
                Data[entry.Name] = entry.Value;
            }

            Data["CloudShell:Configuration:Status"] = "connected";
            Data["CloudShell:Configuration:LoadedKeys"] = string.Join(',', entries.Select(entry => entry.Name));
            Data["CloudShell:Configuration:SecretKeys"] = string.Join(
                ',',
                entries.Where(entry => entry.IsSecret).Select(entry => entry.Name));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            Data["CloudShell:Configuration:Status"] = "unavailable";
            Data["CloudShell:Configuration:Detail"] = exception.Message;
        }
    }

    private static CloudShellConfigurationService? ResolveConfigurationService()
    {
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
                item.Key.EndsWith("_ENDPOINT", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Key.Contains("EXAMPLE_CONFIGURATION", StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var tokenName = $"{name[..^"_ENDPOINT".Length]}_TOKEN";
            if (string.IsNullOrWhiteSpace(endpoint) ||
                !variables.TryGetValue(tokenName, out var token) ||
                string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            return new CloudShellConfigurationService(endpoint, token);
        }

        return null;
    }
}

internal sealed record CloudShellConfigurationService(
    string Endpoint,
    string Token);

internal sealed record CloudShellConfigurationEntry(
    string Name,
    string Value,
    bool IsSecret);
