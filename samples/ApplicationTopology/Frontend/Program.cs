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
    var settings = await client.GetFromJsonAsync<ApplicationSettings>(
        "/settings",
        cancellationToken);
    var database = await client.GetFromJsonAsync<DatabaseCheck>(
        "/database",
        cancellationToken);
    activity?.SetTag("cloudshell.sample.upstream_machine", message?.Machine ?? "unknown");
    activity?.SetTag("cloudshell.sample.configuration_mode", settings?.Mode ?? "unknown");
    activity?.SetTag("cloudshell.sample.database_status", database?.Status ?? "unknown");
    logger.LogInformation(
        ApplicationTopologyLogEvents.ApiResponseReceived,
        "Received message from {UpstreamMachine} with configuration mode {ConfigurationMode} and database status {DatabaseStatus}",
        message?.Machine ?? "unknown",
        settings?.Mode ?? "unknown",
        database?.Status ?? "unknown");

    return Results.Ok(new
    {
        frontend = "Application Topology Frontend",
        logicalApiEndpoint = "https+http://application-topology-api",
        resolvedApiEndpoint = endpoint.ToString(),
        upstream = message,
        settings,
        database
    });
});

app.MapGet("/upstream/failure", async (
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("CloudShell.ApplicationTopology.Frontend");
    using var activity = ApplicationTopologyTraceSources.ActivitySource.StartActivity(
        "frontend.call-application-topology-api-failure",
        ActivityKind.Internal);
    activity?.SetTag("cloudshell.sample.resource", "application-topology-frontend");
    activity?.SetTag("cloudshell.sample.expected_failure", "true");

    var endpoint = configuration.GetRequiredResourceUri("application-topology-api", "http");
    activity?.SetTag("cloudshell.sample.logical_api_endpoint", "https+http://application-topology-api");
    activity?.SetTag("cloudshell.sample.resolved_api_endpoint", endpoint.ToString());
    logger.LogInformation(
        ApplicationTopologyLogEvents.CallingFailingApi,
        "Calling referenced API failure endpoint at {ResolvedEndpoint}",
        endpoint);

    var client = httpClientFactory.CreateClient("application-topology-api");
    var response = await client.GetAsync("/failure", cancellationToken);
    activity?.SetTag("http.response.status_code", ((int)response.StatusCode).ToString());
    activity?.SetTag("cloudshell.sample.upstream_status_code", ((int)response.StatusCode).ToString());

    if (response.IsSuccessStatusCode)
    {
        logger.LogWarning(
            ApplicationTopologyLogEvents.FailingApiResponseReceived,
            "Expected API failure endpoint to fail, but it returned {StatusCode}",
            (int)response.StatusCode);

        return Results.Ok(new
        {
            frontend = "Application Topology Frontend",
            logicalApiEndpoint = "https+http://application-topology-api",
            resolvedApiEndpoint = endpoint.ToString(),
            upstreamStatusCode = (int)response.StatusCode
        });
    }

    activity?.SetStatus(
        ActivityStatusCode.Error,
        $"Application Topology API returned {(int)response.StatusCode}");
    logger.LogWarning(
        ApplicationTopologyLogEvents.FailingApiResponseReceived,
        "Referenced API failed as expected with {StatusCode}",
        (int)response.StatusCode);

    return Results.Problem(
        title: "Intentional upstream failure",
        detail: $"The Application Topology API failure endpoint returned {(int)response.StatusCode}.",
        statusCode: StatusCodes.Status502BadGateway,
        extensions: ApplicationTopologyProblemDetails.CreateFailureExtensions(
            "application-topology-frontend",
            (int)response.StatusCode));
});

app.Run();

internal sealed record ApiMessage(string Message, string Machine);

internal sealed record ApplicationSettings(
    string Message,
    string Mode,
    bool ExternalApiKeyConfigured);

internal sealed record DatabaseCheck(
    string Status,
    string Endpoint,
    string Provider,
    DateTimeOffset DatabaseTimestamp);
