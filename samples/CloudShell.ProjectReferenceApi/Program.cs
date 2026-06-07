var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "Project Reference API",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/message", () => Results.Ok(new
{
    message = "Hello from the referenced API project.",
    machine = Environment.MachineName
}));

app.Run();
