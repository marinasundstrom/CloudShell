using CloudShell.ProjectReference.ServiceDefaults;
using System.Diagnostics;

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

app.MapGet("/message", async (
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("CloudShell.ProjectReference.Api");
    using var activity = ProjectReferenceTraceSources.ActivitySource.StartActivity(
        "api.prepare-message",
        ActivityKind.Internal);
    activity?.SetTag("cloudshell.sample.resource", "project-reference-api");
    logger.LogInformation(
        ProjectReferenceLogEvents.PreparingMessage,
        "Preparing API message on {Machine}",
        Environment.MachineName);

    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);

    var message = new ApiMessage(
        "Hello from the referenced API project.",
        Environment.MachineName);

    activity?.SetTag("cloudshell.sample.machine", message.Machine);
    logger.LogInformation(
        ProjectReferenceLogEvents.MessagePrepared,
        "Prepared API message on {Machine}",
        message.Machine);

    return Results.Ok(message);
});

app.Run();

internal sealed record ApiMessage(string Message, string Machine);
