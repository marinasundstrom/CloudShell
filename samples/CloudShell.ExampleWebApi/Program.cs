using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;
using CloudShell.Secrets.Client;

var builder = CloudShellApplication.CreateBuilder(args);
builder.Configuration.AddCloudShellConfigurationStore();
builder.Configuration.AddCloudShellSecretsVault();
builder.Services.AddServiceDiscovery();
builder.Services.AddHttpClient();
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddServiceDiscovery();
});
builder.Services.AddSingleton<CloudShellResourceCredential>(_ => new DefaultCloudShellResourceCredential());
builder.Services.AddSingleton<CloudShellServiceClients>();

var app = builder.Build();

LogCloudShellConfigurationStatus(app);

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    application = Environment.GetEnvironmentVariable("CLOUDSHELL_APPLICATION") ?? "Example Web API",
    machine = Environment.MachineName,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/configuration", async (
    CloudShellServiceClients clients,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    try
    {
        var client = clients.CreateConfigurationStoreClient();
        var entries = await client.GetEntriesAsync(cancellationToken);
        return Results.Ok(new
        {
            status = "connected",
            detail = (string?)null,
            source = client.EntriesEndpoint.ToString(),
            entries = entries.Select(entry => new
            {
                entry.Name,
                Value = entry.IsSecret ? "Secret" : entry.Value,
                entry.IsSecret
            })
        });
    }
    catch (Exception exception) when (
        exception is CloudShellCredentialUnavailableException or
            CloudShellAuthenticationException or
            HttpRequestException or
            TaskCanceledException)
    {
        if (!string.Equals(
                configuration["CloudShell:ConfigurationStore:Status"],
                "connected",
                StringComparison.OrdinalIgnoreCase) &&
            configuration is IConfigurationRoot root)
        {
            root.Reload();
        }

        var loadedKeys = configuration["CloudShell:ConfigurationStore:LoadedKeys"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        var secretKeys = configuration["CloudShell:ConfigurationStore:SecretKeys"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        return Results.Ok(new
        {
            status = configuration["CloudShell:ConfigurationStore:Status"] ?? "unavailable",
            detail = exception.Message,
            source = configuration["CloudShell:ConfigurationStore:Source"],
            entries = loadedKeys.Select(key => new
            {
                Name = key,
                Value = secretKeys.Contains(key) ? "Secret" : configuration[key],
                IsSecret = secretKeys.Contains(key)
            })
        });
    }
});

app.MapGet("/service-discovery/configuration", async (
    IHttpClientFactory httpClientFactory,
    CloudShellResourceCredential credential,
    CancellationToken cancellationToken) =>
{
    const string logicalEndpoint = "https+http://_entries.sample-app-settings";
    var token = await credential.GetTokenAsync(
        new CloudShellResourceTokenRequest([ConfigurationStoreClient.DefaultScope]),
        cancellationToken);
    using var request = new HttpRequestMessage(HttpMethod.Get, logicalEndpoint);
    request.Headers.Authorization = new("Bearer", token.Token);

    var httpClient = httpClientFactory.CreateClient();
    using var response = await httpClient.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();
    var entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<CloudShellConfigurationEntry>>(
        cancellationToken: cancellationToken) ?? [];

    return Results.Ok(new
    {
        status = "connected",
        source = logicalEndpoint,
        entries = entries.Select(entry => new
        {
            entry.Name,
            Value = entry.IsSecret ? "Secret" : entry.Value,
            entry.IsSecret
        })
    });
});

app.MapGet("/secrets/{name}", async (
    string name,
    CloudShellServiceClients clients,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    try
    {
        var client = clients.CreateSecretsVaultClient();
        var secret = await client.GetSecretAsync(name, cancellationToken: cancellationToken);
        return secret is null
            ? Results.NotFound(new
            {
                status = "notFound",
                source = client.SecretsEndpoint.ToString(),
                name
            })
            : Results.Ok(new
            {
                status = "connected",
                source = client.SecretsEndpoint.ToString(),
                secret.Name,
                secret.Value,
                secret.Version
            });
    }
    catch (Exception exception) when (
        exception is CloudShellCredentialUnavailableException or
            CloudShellAuthenticationException or
            HttpRequestException or
            TaskCanceledException)
    {
        if (!string.Equals(
                configuration["CloudShell:SecretsVault:Status"],
                "connected",
                StringComparison.OrdinalIgnoreCase) &&
            configuration is IConfigurationRoot root)
        {
            root.Reload();
        }

        var value = configuration[name];
        if (!string.IsNullOrEmpty(value))
        {
            return Results.Ok(new
            {
                status = "connected",
                source = configuration["CloudShell:SecretsVault:Source"],
                name,
                value,
                version = (string?)null
            });
        }

        return Results.Ok(new
        {
            status = configuration["CloudShell:SecretsVault:Status"] ?? "unavailable",
            detail = exception.Message,
            source = configuration["CloudShell:SecretsVault:Source"],
            name
        });
    }
});

app.MapGet("/echo/{message}", (string message) => Results.Ok(new
{
    message,
    length = message.Length
}));

app.Run();

static void LogCloudShellConfigurationStatus(WebApplication app)
{
    LogProviderStatus(
        app,
        "Configuration Store",
        "CloudShell:ConfigurationStore",
        "No configuration store service was loaded.");
    LogProviderStatus(
        app,
        "Secrets Vault",
        "CloudShell:SecretsVault",
        "No secrets vault service was loaded.");
}

static void LogProviderStatus(
    WebApplication app,
    string name,
    string metadataPrefix,
    string unavailableMessage)
{
    var status = app.Configuration[$"{metadataPrefix}:Status"];
    if (string.Equals(status, "connected", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogInformation(
            "CloudShell {ProviderName} provider loaded {LoadedKeys} from {Source}.",
            name,
            app.Configuration[$"{metadataPrefix}:LoadedKeys"],
            app.Configuration[$"{metadataPrefix}:Source"]);
        return;
    }

    app.Logger.LogWarning(
        "CloudShell {ProviderName} provider is unavailable. {Detail}",
        name,
        app.Configuration[$"{metadataPrefix}:Detail"] ?? unavailableMessage);
}

sealed class CloudShellServiceClients(CloudShellResourceCredential credential)
{
    public ConfigurationStoreClient CreateConfigurationStoreClient() =>
        ConfigurationStoreClient.FromEnvironment(credential);

    public SecretsVaultClient CreateSecretsVaultClient() =>
        SecretsVaultClient.FromEnvironment(credential);
}
