using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;
using CloudShell.Secrets.Client;

var builder = CloudShellApplication.CreateBuilder(args);
var configurationStoreServiceName = Environment.GetEnvironmentVariable(
    "CLOUDSHELL_CONFIGURATION_SERVICE_NAME");
var secretsVaultName = Environment.GetEnvironmentVariable(
    "CLOUDSHELL_SECRETS_VAULT_NAME");
builder.Configuration.AddCloudShellConfigurationStore(options =>
{
    options.ServiceName = configurationStoreServiceName;
});
builder.Configuration.AddCloudShellSecretsVault(options =>
{
    options.VaultName = secretsVaultName;
});
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
        var settings = await client.GetSettingsAsync(cancellationToken);
        return Results.Ok(new
        {
            status = "connected",
            detail = (string?)null,
            source = client.SettingsEndpoint.ToString(),
            settings = settings.Select(setting => new
            {
                setting.Name,
                setting.Value
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
        return Results.Ok(new
        {
            status = configuration["CloudShell:ConfigurationStore:Status"] ?? "unavailable",
            detail = exception.Message,
            source = configuration["CloudShell:ConfigurationStore:Source"],
            settings = loadedKeys.Select(key => new
            {
                Name = key,
                Value = configuration[key]
            })
        });
    }
});

app.MapGet("/service-discovery/configuration", async (
    IHttpClientFactory httpClientFactory,
    CloudShellResourceCredential credential,
    CancellationToken cancellationToken) =>
{
    const string logicalEndpoint = "https+http://configuration-sample-app";
    var token = await credential.GetTokenAsync(
        new CloudShellResourceTokenRequest([ConfigurationStoreClient.DefaultScope]),
        cancellationToken);
    using var request = new HttpRequestMessage(HttpMethod.Get, logicalEndpoint);
    request.Headers.Authorization = new("Bearer", token.Token);

    var httpClient = httpClientFactory.CreateClient();
    using var response = await httpClient.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();
    var settings = await response.Content.ReadFromJsonAsync<IReadOnlyList<CloudShellConfigurationSetting>>(
        cancellationToken: cancellationToken) ?? [];

    return Results.Ok(new
    {
        status = "connected",
        source = logicalEndpoint,
    settings = settings.Select(setting => new
    {
        setting.Name,
        setting.Value
    })
    });
});

app.MapGet("/service-discovery/configuration-store", async (
    IHttpClientFactory httpClientFactory,
    CloudShellResourceCredential credential,
    CancellationToken cancellationToken) =>
{
    var storeId = Environment.GetEnvironmentVariable(
        "CLOUDSHELL_CONFIGURATION_SAMPLE_APP_STORE_ID");
    if (string.IsNullOrWhiteSpace(storeId))
    {
        return Results.Problem(
            "The configuration store id is not configured.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var logicalEndpoint =
        $"https+http://configuration.store-sample-app/api/configuration/stores/{Uri.EscapeDataString(storeId)}/entries";
    var token = await credential.GetTokenAsync(
        new CloudShellResourceTokenRequest([ConfigurationStoreClient.DefaultScope]),
        cancellationToken);
    using var request = new HttpRequestMessage(HttpMethod.Get, logicalEndpoint);
    request.Headers.Authorization = new("Bearer", token.Token);

    var httpClient = httpClientFactory.CreateClient();
    using var response = await httpClient.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();
    var settings = await response.Content.ReadFromJsonAsync<IReadOnlyList<CloudShellConfigurationSetting>>(
        cancellationToken: cancellationToken) ?? [];

    return Results.Ok(new
    {
        status = "connected",
        source = logicalEndpoint,
    settings = settings.Select(setting => new
    {
        setting.Name,
        setting.Value
    })
    });
});

app.MapGet("/service-discovery/secrets-vault/{name}", async (
    string name,
    IHttpClientFactory httpClientFactory,
    CloudShellResourceCredential credential,
    CancellationToken cancellationToken) =>
{
    var vaultId = Environment.GetEnvironmentVariable(
        "CLOUDSHELL_SECRETS_SAMPLE_APP_VAULT_ID");
    if (string.IsNullOrWhiteSpace(vaultId))
    {
        return Results.Problem(
            "The Secrets Vault id is not configured.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var logicalEndpoint =
        $"https+http://secrets.vault-sample-app/api/secrets/vaults/{Uri.EscapeDataString(vaultId)}/secrets/{Uri.EscapeDataString(name)}";
    var token = await credential.GetTokenAsync(
        new CloudShellResourceTokenRequest([SecretsVaultClient.DefaultScope]),
        cancellationToken);
    using var request = new HttpRequestMessage(HttpMethod.Get, logicalEndpoint);
    request.Headers.Authorization = new("Bearer", token.Token);

    var httpClient = httpClientFactory.CreateClient();
    using var response = await httpClient.SendAsync(request, cancellationToken);
    response.EnsureSuccessStatusCode();
    var secret = await response.Content.ReadFromJsonAsync<SecretValue>(
        cancellationToken: cancellationToken);

    return secret is null
        ? Results.NotFound(new
        {
            status = "notFound",
            source = logicalEndpoint,
            name
        })
        : Results.Ok(new
        {
            status = "connected",
            source = logicalEndpoint,
            secret.Name,
            secret.Value,
            secret.Version
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
        ConfigurationStoreClient.FromEnvironment(
            credential,
            Environment.GetEnvironmentVariable("CLOUDSHELL_CONFIGURATION_SERVICE_NAME"));

    public SecretsVaultClient CreateSecretsVaultClient() =>
        SecretsVaultClient.FromEnvironment(
            credential,
            Environment.GetEnvironmentVariable("CLOUDSHELL_SECRETS_VAULT_NAME"));
}
