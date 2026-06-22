using CloudShell.ReplicatedContainerHealth.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.UseCloudShellMetrics();

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", (ILogger<Program> logger) =>
{
    var resourceId = GetResourceId();
    var replica = GetReplicaOrdinal();
    using var activity = ReplicatedContainerHealthTraceSources.ActivitySource.StartActivity("Report health");
    activity?.SetTag("cloudshell.resource.id", resourceId);
    activity?.SetTag("runtime.replica.ordinal", replica);

    logger.LogInformation(
        ReplicatedContainerHealthLogEvents.HealthChecked,
        "Replica {Replica} reported healthy for {ResourceId}.",
        replica,
        resourceId);

    return Results.Ok(new
    {
        status = "healthy",
        resource = resourceId,
        replica,
        machine = Environment.MachineName,
        timestamp = DateTimeOffset.UtcNow
    });
});

app.MapGet("/alive", (ILogger<Program> logger) =>
{
    var replica = GetReplicaOrdinal();
    using var activity = ReplicatedContainerHealthTraceSources.ActivitySource.StartActivity("Report liveness");
    activity?.SetTag("runtime.replica.ordinal", replica);

    logger.LogInformation(
        ReplicatedContainerHealthLogEvents.LivenessChecked,
        "Replica {Replica} reported alive.",
        replica);

    return Results.Ok(new
    {
        status = "alive",
        replica,
        timestamp = DateTimeOffset.UtcNow
    });
});

app.MapGet("/work", async (ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    var resourceId = GetResourceId();
    var replica = GetReplicaOrdinal();
    using var activity = ReplicatedContainerHealthTraceSources.ActivitySource.StartActivity("Handle demo work");
    activity?.SetTag("cloudshell.resource.id", resourceId);
    activity?.SetTag("runtime.replica.ordinal", replica);

    await Task.Delay(Random.Shared.Next(20, 75), cancellationToken);

    logger.LogInformation(
        ReplicatedContainerHealthLogEvents.DemoWorkHandled,
        "Replica {Replica} handled demo work for {ResourceId}.",
        replica,
        resourceId);

    return Results.Ok(new
    {
        status = "handled",
        resource = resourceId,
        replica,
        timestamp = DateTimeOffset.UtcNow
    });
});

app.Run();

static string GetResourceId() =>
    Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID") ?? "application:api";

static string GetReplicaOrdinal() =>
    Environment.GetEnvironmentVariable("CLOUDSHELL_REPLICA_ORDINAL") ?? "1";
