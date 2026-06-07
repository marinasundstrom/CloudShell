using CloudShell.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/upstream"));

app.MapGet("/upstream", async (
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    var endpoint = configuration.GetResourceEndpoint("project-reference-api", "http");
    if (endpoint is null)
    {
        return Results.Problem(
            "The referenced API endpoint was not found in service discovery configuration.");
    }

    var client = httpClientFactory.CreateClient();
    var message = await client.GetFromJsonAsync<ApiMessage>(
        new Uri(endpoint, "/message"),
        cancellationToken);

    return Results.Ok(new
    {
        frontend = "Project Reference Frontend",
        resolvedApiEndpoint = endpoint.ToString(),
        upstream = message
    });
});

app.Run();

internal sealed record ApiMessage(string Message, string Machine);
