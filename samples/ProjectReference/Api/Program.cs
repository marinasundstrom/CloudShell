using CloudShell.ProjectReference.ServiceDefaults;

var builder = CloudShellApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health"));
app.MapDefaultEndpoints();

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
