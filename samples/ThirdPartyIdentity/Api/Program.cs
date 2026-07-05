using CloudShell.Client.Authentication;
using CloudShell.Configuration.Client;

var builder = CloudShellApplication.CreateBuilder(args);

builder.Services.AddSingleton<CloudShellResourceCredential>(_ => new DefaultCloudShellResourceCredential());

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    application = "CloudShell Third-party Identity API",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/configuration", async (
    CloudShellResourceCredential credential,
    CancellationToken cancellationToken) =>
{
    try
    {
        var client = ConfigurationStoreClient.TryCreateFromEnvironment(
            credential,
            scopes: []);
        if (client is null)
        {
            return Results.Ok(new
            {
                status = "unavailable",
                detail = "No CloudShell configuration store endpoint was injected.",
                source = (string?)null,
                settings = Array.Empty<object>()
            });
        }

        var settings = await client.GetSettingsAsync(cancellationToken);
        return Results.Ok(new
        {
            status = "connected",
            detail = (string?)null,
            source = client.SettingsEndpoint.ToString(),
            clientId = Environment.GetEnvironmentVariable(
                EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable),
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
        return Results.Ok(new
        {
            status = "unavailable",
            detail = exception.Message,
            source = Environment.GetEnvironmentVariables()
                .Keys
                .OfType<string>()
                .Where(key => key.StartsWith("CLOUDSHELL_CONFIGURATION_", StringComparison.OrdinalIgnoreCase))
                .Where(key => key.EndsWith("_ENDPOINT", StringComparison.OrdinalIgnoreCase))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .Select(Environment.GetEnvironmentVariable)
                .FirstOrDefault(),
            settings = Array.Empty<object>()
        });
    }
});

app.Run();
