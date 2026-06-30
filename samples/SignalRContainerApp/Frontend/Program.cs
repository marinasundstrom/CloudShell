var builder = WebApplication.CreateBuilder(args);

var backendBaseUrl = builder.Configuration["SignalRBackend:BaseUrl"] ??
    "http://localhost:5095";

var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    backendBaseUrl
}));
app.MapGet("/alive", () => Results.Ok(new
{
    status = "alive",
    backendBaseUrl
}));
app.MapGet("/sample-config.json", () => Results.Json(new SignalRContainerAppClientOptions(
    backendBaseUrl.TrimEnd('/'))));
app.MapFallbackToFile("index.html");

app.Run();

internal sealed record SignalRContainerAppClientOptions(
    string BackendBaseUrl);
