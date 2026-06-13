using CloudShell.ProjectReference.ServiceDefaults;
using System.Diagnostics;

var builder = CloudShellApplication.CreateBuilder(args);
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
    using var activity = ProjectReferenceTraceSources.ActivitySource.StartActivity(
        "frontend.call-project-reference-api",
        ActivityKind.Internal);
    activity?.SetTag("cloudshell.sample.resource", "project-reference-frontend");

    var endpoint = configuration.GetRequiredResourceUri("project-reference-api", "http");
    activity?.SetTag("cloudshell.sample.logical_api_endpoint", "https+http://project-reference-api");
    activity?.SetTag("cloudshell.sample.resolved_api_endpoint", endpoint.ToString());

    var client = httpClientFactory.CreateClient("project-reference-api");
    var message = await client.GetFromJsonAsync<ApiMessage>(
        "/message",
        cancellationToken);
    activity?.SetTag("cloudshell.sample.upstream_machine", message?.Machine ?? "unknown");

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
