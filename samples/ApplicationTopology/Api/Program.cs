using CloudShell.ApplicationTopology.ServiceDefaults;
using System.Diagnostics;

var builder = CloudShellApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health"));
app.MapDefaultEndpoints();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "Application Topology API",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/message", async (
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("CloudShell.ApplicationTopology.Api");
    using var activity = ApplicationTopologyTraceSources.ActivitySource.StartActivity(
        "api.prepare-message",
        ActivityKind.Internal);
    activity?.SetTag("cloudshell.sample.resource", "application-topology-api");
    logger.LogInformation(
        ApplicationTopologyLogEvents.PreparingMessage,
        "Preparing API message on {Machine}",
        Environment.MachineName);

    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);

    var message = new ApiMessage(
        "Hello from the referenced API project.",
        Environment.MachineName);

    activity?.SetTag("cloudshell.sample.machine", message.Machine);
    logger.LogInformation(
        ApplicationTopologyLogEvents.MessagePrepared,
        "Prepared API message on {Machine}",
        message.Machine);

    return Results.Ok(message);
});

app.Run();

internal sealed record ApiMessage(string Message, string Machine);
