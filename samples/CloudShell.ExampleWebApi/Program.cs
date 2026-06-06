var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    application = Environment.GetEnvironmentVariable("CLOUDSHELL_APPLICATION") ?? "Example Web API",
    machine = Environment.MachineName,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/echo/{message}", (string message) => Results.Ok(new
{
    message,
    length = message.Length
}));

app.Run();
