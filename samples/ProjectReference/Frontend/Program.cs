using CloudShell.ProjectReference.ServiceDefaults;

var builder = SampleHostSettings.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddResourceHttpClient(
    "project-reference-api",
    "project-reference-api",
    "http");

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Redirect("/upstream"));

app.MapGet("/upstream", async (
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    var endpoint = configuration.GetRequiredResourceUri("project-reference-api", "http");
    var client = httpClientFactory.CreateClient("project-reference-api");
    var message = await client.GetFromJsonAsync<ApiMessage>(
        "/message",
        cancellationToken);

    return Results.Ok(new
    {
        frontend = "Project Reference Frontend",
        logicalApiEndpoint = "https+http://project-reference-api",
        resolvedApiEndpoint = endpoint.ToString(),
        upstream = message
    });
});

app.Run();

internal sealed record ApiMessage(string Message, string Machine);
