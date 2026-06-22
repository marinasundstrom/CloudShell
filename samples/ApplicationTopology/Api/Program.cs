using CloudShell.ApplicationTopology.ServiceDefaults;
using CloudShell.SqlServer.Client;
using Microsoft.Data.SqlClient;
using System.Diagnostics;

var builder = CloudShellApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCloudShellSqlServerClient(options =>
{
    options.SqlServerResourceName = "application-topology-sql-server";
});

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
    CloudShellSqlConnectionFactory sqlConnections,
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

    await using var connection = await CreateSqlConnectionAsync(
        configuration,
        endpoint,
        sqlConnections,
        cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT DB_NAME(), SYSDATETIMEOFFSET()";
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken))
    {
        throw new InvalidOperationException("SQL Server did not return a database check row.");
    }

    var databaseName = reader.GetString(0);
    var databaseTimestamp = reader.GetDateTimeOffset(1);

    activity?.SetTag("db.system", "mssql");
    activity?.SetTag("db.name", databaseName);
    activity?.SetTag("db.operation.name", "SELECT");
    logger.LogInformation(
        ApplicationTopologyLogEvents.DatabaseChecked,
        "Checked SQL Server dependency at {SqlEndpoint} using database {DatabaseName}",
        endpoint,
        databaseName);

    return Results.Ok(new DatabaseCheck(
        "ok",
        endpoint.ToString(),
        "mssql",
        databaseName,
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
        statusCode: StatusCodes.Status500InternalServerError,
        extensions: ApplicationTopologyProblemDetails.CreateFailureExtensions("application-topology-api"));
});

app.Run();

static async ValueTask<SqlConnection> CreateSqlConnectionAsync(
    IConfiguration configuration,
    Uri endpoint,
    CloudShellSqlConnectionFactory sqlConnections,
    CancellationToken cancellationToken)
{
    if (UsesCloudShellSqlAuthentication(configuration))
    {
        var databaseName = GetSqlDatabaseName(configuration);
        return await sqlConnections.OpenConnectionAsync(
            "application-topology-sql-server",
            databaseName,
            cancellationToken);
    }

    var connection = new SqlConnection(CreateSqlConnectionString(configuration, endpoint));
    await connection.OpenAsync(cancellationToken);
    return connection;
}

static string CreateSqlConnectionString(IConfiguration configuration, Uri endpoint)
{
    var builder = new SqlConnectionStringBuilder
    {
        DataSource = CreateSqlDataSource(endpoint),
        UserID = configuration["ApplicationTopology:SqlServer:User"] ?? "sa",
        Password = configuration["ApplicationTopology:SqlServer:Password"]
            ?? throw new InvalidOperationException(
                "ApplicationTopology:SqlServer:Password is required."),
        InitialCatalog = GetSqlDatabaseName(configuration),
        Encrypt = false,
        TrustServerCertificate = true,
        ConnectTimeout = 5
    };
    return builder.ConnectionString;
}

static bool UsesCloudShellSqlAuthentication(IConfiguration configuration) =>
    string.Equals(
        configuration["ApplicationTopology:SqlServer:Authentication"],
        "CloudShell",
        StringComparison.OrdinalIgnoreCase);

static string GetSqlDatabaseName(IConfiguration configuration) =>
    configuration["ApplicationTopology:SqlServer:Database"] ?? "application_topology";

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
    string Database,
    DateTimeOffset DatabaseTimestamp);
