using CloudShell.Abstractions.Authentication;
using CloudShell.Configuration;
using CloudShell.Secrets;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddCloudShellConfiguration();

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

app.MapGet("/configuration", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    try
    {
        var client = ConfigurationStoreClient.FromEnvironment(
            new DefaultCloudShellResourceCredential());
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
                configuration["CloudShell:Configuration:Status"],
                "connected",
                StringComparison.OrdinalIgnoreCase) &&
            configuration is IConfigurationRoot root)
        {
            root.Reload();
        }

        var loadedKeys = configuration["CloudShell:Configuration:LoadedKeys"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        var secretKeys = configuration["CloudShell:Configuration:SecretKeys"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        return Results.Ok(new
        {
            status = configuration["CloudShell:Configuration:Status"] ?? "unavailable",
            detail = exception.Message,
            source = configuration["CloudShell:Configuration:Source"],
            entries = loadedKeys.Select(key => new
            {
                Name = key,
                Value = secretKeys.Contains(key) ? "Secret" : configuration[key],
                IsSecret = secretKeys.Contains(key)
            })
        });
    }
});

app.MapGet("/secrets/{name}", async (string name, CancellationToken cancellationToken) =>
{
    try
    {
        var client = SecretsVaultClient.FromEnvironment(
            new DefaultCloudShellResourceCredential());
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
        return Results.Ok(new
        {
            status = "unavailable",
            detail = exception.Message,
            source = (string?)null,
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
    var status = app.Configuration["CloudShell:Configuration:Status"];
    if (string.Equals(status, "connected", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogInformation(
            "CloudShell configuration provider loaded {LoadedKeys} from {Source}.",
            app.Configuration["CloudShell:Configuration:LoadedKeys"],
            app.Configuration["CloudShell:Configuration:Source"]);
        return;
    }

    app.Logger.LogWarning(
        "CloudShell configuration provider is unavailable. {Detail}",
        app.Configuration["CloudShell:Configuration:Detail"] ?? "No configuration service was loaded.");
}
