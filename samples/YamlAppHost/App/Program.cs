var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => new
{
    message = "Hello from the CloudShell YAML sample",
    processId = Environment.ProcessId
});
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
