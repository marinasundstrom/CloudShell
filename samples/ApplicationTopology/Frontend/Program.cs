using CloudShell.ApplicationTopology.ServiceDefaults;
using System.Diagnostics;

var builder = CloudShellApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddResourceHttpClient(
    "application-topology-api",
    "application-topology-api",
    "http");

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Redirect("/upstream"));

app.MapGet("/upstream", async (
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("CloudShell.ApplicationTopology.Frontend");
    using var activity = ApplicationTopologyTraceSources.ActivitySource.StartActivity(
        "frontend.call-application-topology-api",
        ActivityKind.Internal);
    activity?.SetTag("cloudshell.sample.resource", "application-topology-frontend");

    var endpoint = configuration.GetRequiredResourceUri("application-topology-api", "http");
    activity?.SetTag("cloudshell.sample.logical_api_endpoint", "https+http://application-topology-api");
    activity?.SetTag("cloudshell.sample.resolved_api_endpoint", endpoint.ToString());
    logger.LogInformation(
        ApplicationTopologyLogEvents.CallingApi,
        "Calling referenced API at {ResolvedEndpoint}",
        endpoint);

    var client = httpClientFactory.CreateClient("application-topology-api");
    var message = await client.GetFromJsonAsync<ApiMessage>(
        "/message",
        cancellationToken);
    activity?.SetTag("cloudshell.sample.upstream_machine", message?.Machine ?? "unknown");
    logger.LogInformation(
        ApplicationTopologyLogEvents.ApiResponseReceived,
        "Received message from {UpstreamMachine}",
        message?.Machine ?? "unknown");

    return Results.Ok(new
    {
        frontend = "Application Topology Frontend",
        logicalApiEndpoint = "https+http://application-topology-api",
        resolvedApiEndpoint = endpoint.ToString(),
        upstream = message
    });
});

app.Run();

internal sealed record ApiMessage(string Message, string Machine);
