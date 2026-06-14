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
                entries = Array.Empty<object>()
            });
        }

        var entries = await client.GetEntriesAsync(cancellationToken);
        return Results.Ok(new
        {
            status = "connected",
            detail = (string?)null,
            source = client.EntriesEndpoint.ToString(),
            clientId = Environment.GetEnvironmentVariable(
                EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable),
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
            entries = Array.Empty<object>()
        });
    }
});

app.Run();
