using CloudShell.Configuration;

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

app.MapGet("/configuration", (IConfiguration configuration) =>
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
        detail = configuration["CloudShell:Configuration:Detail"],
        source = configuration["CloudShell:Configuration:Source"],
        entries = loadedKeys.Select(key => new
        {
            Name = key,
            Value = secretKeys.Contains(key) ? "Secret" : configuration[key],
            IsSecret = secretKeys.Contains(key)
        })
    });
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
