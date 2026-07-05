using CloudShell.Abstractions.Authorization;
using CloudShell.ControlPlane.Authentication;
using CloudShell.EventBroker.Client;
using CloudShell.EventBrokerService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<EventBrokerServiceOptions>(
    builder.Configuration.GetSection(EventBrokerServiceOptions.SectionName));
builder.Services.Configure<CloudShellAuthenticationOptions>(
    builder.Configuration.GetSection(CloudShellAuthenticationOptions.SectionName));
builder.Services.AddSingleton<EventBrokerServiceStore>();
builder.Services.AddSingleton<BuiltInAuthorityTokenService>();
builder.Services.AddSingleton<CloudShellBearerTokenValidationService>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "Healthy",
    service = "CloudShell Event Broker Service"
}))
.AllowAnonymous();

app.UseCloudShellServiceBearerAuthentication();

var api = app
    .MapGroup("/api/events")
    .WithTags("Events");

api.MapGet("/brokers/{brokerId}/streams", (
    string brokerId,
    HttpRequest request,
    EventBrokerServiceStore store) =>
{
    var broker = store.GetBroker(brokerId);
    if (!HasBearerToken(request))
    {
        return Unauthorized();
    }

    if (broker is null ||
        !IsAuthorized(broker, request, EventBrokerResourceOperationPermissions.ReadEvents))
    {
        return NotFound();
    }

    try
    {
        return Results.Ok(store.ListStreams(brokerId));
    }
    catch (EventBrokerNotFoundException)
    {
        return NotFound();
    }
});

api.MapGet("/brokers/{brokerId}/streams/{stream}/events", (
    string brokerId,
    string stream,
    long? fromSequence,
    int? limit,
    HttpRequest request,
    EventBrokerServiceStore store) =>
{
    var broker = store.GetBroker(brokerId);
    if (!HasBearerToken(request))
    {
        return Unauthorized();
    }

    if (broker is null ||
        !IsAuthorized(broker, request, EventBrokerResourceOperationPermissions.ReadEvents))
    {
        return NotFound();
    }

    try
    {
        return Results.Ok(store.Read(
            brokerId,
            stream,
            Math.Max(0, fromSequence ?? 0),
            limit ?? 100));
    }
    catch (EventBrokerNotFoundException)
    {
        return NotFound();
    }
});

api.MapPost("/brokers/{brokerId}/streams/{stream}/events", (
    string brokerId,
    string stream,
    EventBrokerPublishRequest request,
    HttpRequest httpRequest,
    EventBrokerServiceStore store) =>
{
    var broker = store.GetBroker(brokerId);
    if (!HasBearerToken(httpRequest))
    {
        return Unauthorized();
    }

    if (broker is null ||
        !IsAuthorized(broker, httpRequest, EventBrokerResourceOperationPermissions.PublishEvents))
    {
        return NotFound();
    }

    if (string.IsNullOrWhiteSpace(request.Type))
    {
        return Results.Problem(
            "Event type is required.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid event");
    }

    try
    {
        return Results.Ok(store.Append(brokerId, stream, request));
    }
    catch (EventBrokerNotFoundException)
    {
        return NotFound();
    }
});

app.Run();

static bool IsAuthorized(
    EventBrokerDefinition broker,
    HttpRequest request,
    string permission) =>
    ResourcePermissionClaimAuthorization.HasResourcePermission(
        request.HttpContext.User,
        broker.Id,
        permission);

static bool HasBearerToken(HttpRequest request)
{
    var authorization = request.Headers.Authorization.FirstOrDefault();
    const string bearerPrefix = "Bearer ";
    return authorization?.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) == true &&
        !string.IsNullOrWhiteSpace(authorization[bearerPrefix.Length..]);
}

static IResult Unauthorized() =>
    Results.Problem(
        "An Event Broker bearer token is required.",
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized");

static IResult NotFound() =>
    Results.Problem(
        "The Event Broker was not found.",
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found");
