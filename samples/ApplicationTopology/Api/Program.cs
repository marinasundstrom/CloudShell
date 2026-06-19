using CloudShell.ApplicationTopology.ServiceDefaults;
using Microsoft.Data.SqlClient;
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

app.MapGet("/settings", (IConfiguration configuration) =>
{
    var message = configuration["ApplicationTopology:Message"];
    var mode = configuration["ApplicationTopology:Mode"];
    var externalApiKey = configuration["ApplicationTopology:ExternalApiKey"];

    return Results.Ok(new ApplicationSettings(
        message ?? "not configured",
        mode ?? "not configured",
        !string.IsNullOrWhiteSpace(externalApiKey)));
});

app.MapGet("/database", async (
    IConfiguration configuration,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("CloudShell.ApplicationTopology.Api");
    using var activity = ApplicationTopologyTraceSources.ActivitySource.StartActivity(
        "api.check-sql-server",
        ActivityKind.Client);
    activity?.SetTag("cloudshell.sample.resource", "application-topology-api");
    activity?.SetTag("cloudshell.sample.dependency", "application-topology-sql-server");

    var endpoint = configuration.GetRequiredResourceUri("application-topology-sql-server", "tds");
    activity?.SetTag("cloudshell.sample.sql_endpoint", endpoint.ToString());
    logger.LogInformation(
        ApplicationTopologyLogEvents.CheckingDatabase,
        "Checking SQL Server dependency at {SqlEndpoint}",
        endpoint);

    await using var connection = new SqlConnection(CreateSqlConnectionString(configuration, endpoint));
    await connection.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT SYSDATETIMEOFFSET()";
    var databaseTimestamp = (DateTimeOffset)(await command.ExecuteScalarAsync(cancellationToken)
        ?? throw new InvalidOperationException("SQL Server did not return a timestamp."));

    activity?.SetTag("db.system", "mssql");
    activity?.SetTag("db.operation.name", "SELECT");
    logger.LogInformation(
        ApplicationTopologyLogEvents.DatabaseChecked,
        "Checked SQL Server dependency at {SqlEndpoint}",
        endpoint);

    return Results.Ok(new DatabaseCheck(
        "ok",
        endpoint.ToString(),
        "mssql",
        databaseTimestamp));
});

app.MapGet("/failure", (ILoggerFactory loggerFactory) =>
{
    var logger = loggerFactory.CreateLogger("CloudShell.ApplicationTopology.Api");
    using var activity = ApplicationTopologyTraceSources.ActivitySource.StartActivity(
        "api.intentional-failure",
        ActivityKind.Internal);
    activity?.SetTag("cloudshell.sample.resource", "application-topology-api");
    activity?.SetTag("cloudshell.sample.failure", "intentional");
    activity?.SetStatus(ActivityStatusCode.Error, "Intentional sample failure");

    logger.LogError(
        ApplicationTopologyLogEvents.IntentionalFailureInvoked,
        "Intentional sample failure endpoint invoked on {Machine}",
        Environment.MachineName);

    return Results.Problem(
        title: "Intentional sample failure",
        detail: "The Application Topology API failed deliberately so CloudShell can demonstrate failed request telemetry.",
        statusCode: StatusCodes.Status500InternalServerError);
});

app.Run();

static string CreateSqlConnectionString(IConfiguration configuration, Uri endpoint)
{
    var builder = new SqlConnectionStringBuilder
    {
        DataSource = CreateSqlDataSource(endpoint),
        UserID = configuration["ApplicationTopology:SqlServer:User"] ?? "sa",
        Password = configuration["ApplicationTopology:SqlServer:Password"]
            ?? throw new InvalidOperationException(
                "ApplicationTopology:SqlServer:Password is required."),
        InitialCatalog = "master",
        Encrypt = false,
        TrustServerCertificate = true,
        ConnectTimeout = 5
    };
    return builder.ConnectionString;
}

static string CreateSqlDataSource(Uri endpoint)
{
    var host = endpoint.Host;
    if (string.IsNullOrWhiteSpace(host))
    {
        throw new InvalidOperationException(
            $"SQL Server endpoint '{endpoint}' does not include a host.");
    }

    return endpoint.Port > 0
        ? $"{host},{endpoint.Port}"
        : host;
}

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
