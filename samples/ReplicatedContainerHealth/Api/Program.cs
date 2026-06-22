var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    resource = Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID") ?? "application:api",
    replica = Environment.GetEnvironmentVariable("CLOUDSHELL_REPLICA_ORDINAL") ?? "1",
    machine = Environment.MachineName,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/alive", () => Results.Ok(new
{
    status = "alive",
    replica = Environment.GetEnvironmentVariable("CLOUDSHELL_REPLICA_ORDINAL") ?? "1",
    timestamp = DateTimeOffset.UtcNow
}));

app.Run();
