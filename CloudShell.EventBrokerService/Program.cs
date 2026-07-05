using CloudShell.EventBroker.Client;
using CloudShell.EventBrokerService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<EventBrokerServiceOptions>(
    builder.Configuration.GetSection(EventBrokerServiceOptions.SectionName));
builder.Services.AddSingleton<EventBrokerServiceStore>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "Healthy",
    service = "CloudShell Event Broker Service"
}));

var api = app
    .MapGroup("/api/events")
    .WithTags("Events");

api.MapGet("/brokers/{brokerId}/streams", (
    string brokerId,
    EventBrokerServiceStore store) =>
{
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
    EventBrokerServiceStore store) =>
{
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
    EventBrokerServiceStore store) =>
{
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

static IResult NotFound() =>
    Results.Problem(
        "The Event Broker was not found.",
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found");
