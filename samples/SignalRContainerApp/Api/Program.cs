using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId |
        ActivityTrackingOptions.SpanId |
        ActivityTrackingOptions.ParentId;
});
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "O";
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true));
});
builder.Services.AddHttpClient<CloudShellTraceExporter>();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(SignalRTraceSources.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddProcessor(serviceProvider =>
                new BatchActivityExportProcessor(
                    serviceProvider.GetRequiredService<CloudShellTraceExporter>()));
    });
builder.Services.AddSignalR();

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => Results.Redirect("/health"));
app.MapGet("/health", () => Results.Ok(SignalRReplicaRuntime.CreateSnapshot("healthy")));
app.MapGet("/alive", () => Results.Ok(SignalRReplicaRuntime.CreateSnapshot("alive")));
app.MapGet("/replica", () => Results.Ok(SignalRReplicaRuntime.CreateSnapshot("current")));
app.MapHub<ReplicaHub>("/hubs/replicas");

app.Run();

public sealed class ReplicaHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        using var activity = SignalRTraceSources.ActivitySource.StartActivity(
            "SignalR connect",
            ActivityKind.Server);
        SignalRObservability.EnrichSignalRActivity(
            activity,
            Context.ConnectionId,
            "connect");
        SignalRObservability.RecordSignalRMetric(
            "signalr.server.connections",
            1,
            "count",
            Context.ConnectionId,
            "connect");

        await Clients.Caller.SendAsync(
            "ReplicaConnected",
            CreateMessage("Connected to SignalR backend."));
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        using var activity = SignalRTraceSources.ActivitySource.StartActivity(
            "SignalR disconnect",
            ActivityKind.Server);
        SignalRObservability.EnrichSignalRActivity(
            activity,
            Context.ConnectionId,
            "disconnect");
        if (exception is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("error.type", exception.GetType().FullName);
        }

        SignalRObservability.RecordSignalRMetric(
            "signalr.server.disconnections",
            1,
            "count",
            Context.ConnectionId,
            "disconnect");

        return base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(string text)
    {
        using var activity = SignalRTraceSources.ActivitySource.StartActivity(
            "SignalR message broadcast",
            ActivityKind.Server);
        SignalRObservability.EnrichSignalRActivity(
            activity,
            Context.ConnectionId,
            "message");
        activity?.SetTag("signalr.message.length", text?.Length ?? 0);
        SignalRObservability.RecordSignalRMetric(
            "signalr.server.messages",
            1,
            "count",
            Context.ConnectionId,
            "message");

        await Clients.All.SendAsync(
            "ReplicaMessage",
            CreateMessage(string.IsNullOrWhiteSpace(text) ? "Ping" : text.Trim()));
    }

    private SignalRReplicaMessage CreateMessage(string text) =>
        new(
            text,
            SignalRReplicaRuntime.GetReplicaOrdinal(),
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID") ?? "application.container-app:signalr-api",
            Environment.MachineName,
            Context.ConnectionId,
            DateTimeOffset.UtcNow);
}

internal static class SignalRReplicaRuntime
{
    public static ReplicaSnapshot CreateSnapshot(string status) =>
        new(
            status,
            GetReplicaOrdinal(),
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID") ?? "application.container-app:signalr-api",
            Environment.MachineName,
            DateTimeOffset.UtcNow);

    public static string GetReplicaOrdinal() =>
        Environment.GetEnvironmentVariable("CLOUDSHELL_REPLICA_ORDINAL") ?? "1";
}

public sealed record ReplicaSnapshot(
    string Status,
    string Replica,
    string Resource,
    string Machine,
    DateTimeOffset Timestamp);

public sealed record SignalRReplicaMessage(
    string Text,
    string Replica,
    string Resource,
    string Machine,
    string ConnectionId,
    DateTimeOffset Timestamp);

public static class SignalRTraceSources
{
    public const string ActivitySourceName = "CloudShell.SignalRContainerApp.SignalR";

    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}

internal static class SignalRObservability
{
    private static readonly HttpClient MetricHttpClient = new();

    public static void EnrichSignalRActivity(
        Activity? activity,
        string connectionId,
        string eventName)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("signalr.hub", nameof(ReplicaHub));
        activity.SetTag("signalr.event", eventName);
        activity.SetTag("signalr.connection.id", connectionId);
        activity.SetTag("runtime.replica.ordinal", SignalRReplicaRuntime.GetReplicaOrdinal());
        activity.SetTag(
            "cloudshell.resource.id",
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID") ??
            "application.container-app:signalr-api");
    }

    public static void RecordSignalRMetric(
        string name,
        double value,
        string unit,
        string connectionId,
        string eventName)
    {
        var endpoint = TryCreateEndpoint(
            Environment.GetEnvironmentVariable("CLOUDSHELL_METRIC_INGEST_ENDPOINT"));
        if (endpoint is null)
        {
            return;
        }

        var serviceName = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            "signalr-api");
        var resourceId = FirstNonEmpty(
            Environment.GetEnvironmentVariable("CLOUDSHELL_TELEMETRY_RESOURCE_ID"),
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID"),
            serviceName);
        var attributes = new Dictionary<string, string>(
            ParseOtelResourceAttributes(Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES")),
            StringComparer.OrdinalIgnoreCase)
        {
            ["signalr.hub"] = nameof(ReplicaHub),
            ["signalr.event"] = eventName,
            ["signalr.connection.id"] = connectionId,
            ["runtime.replica.ordinal"] = SignalRReplicaRuntime.GetReplicaOrdinal()
        };
        var point = new CloudShellMetricPoint(
            name,
            resourceId,
            serviceName,
            value,
            DateTimeOffset.UtcNow,
            unit,
            attributes);

        _ = Task.Run(() => PostMetricPointsAsync(endpoint, [point]));
    }

    public static async Task PostMetricPointsAsync(
        Uri endpoint,
        IReadOnlyList<CloudShellMetricPoint> points)
    {
        try
        {
            using var scope = SuppressInstrumentationScope.Begin();
            await MetricHttpClient.PostAsJsonAsync(endpoint, new CloudShellMetricIngestRequest(points));
        }
        catch
        {
        }
    }

    public static IReadOnlyDictionary<string, string> ParseOtelResourceAttributes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in SplitEscaped(value, ','))
        {
            var separatorIndex = IndexOfUnescaped(token, '=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = token[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            attributes[key] = Unescape(token[(separatorIndex + 1)..]);
        }

        return attributes;
    }

    public static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? "unknown-service";

    public static Uri? TryCreateEndpoint(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;

    private static IEnumerable<string> SplitEscaped(string value, char separator)
    {
        var start = 0;
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current == separator)
            {
                yield return value[start..index];
                start = index + 1;
            }
        }

        yield return value[start..];
    }

    private static int IndexOfUnescaped(string value, char target)
    {
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current == target)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Unescape(string value)
    {
        var result = new char[value.Length];
        var count = 0;
        var escaped = false;
        foreach (var current in value)
        {
            if (escaped)
            {
                result[count++] = current;
                escaped = false;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                continue;
            }

            result[count++] = current;
        }

        if (escaped)
        {
            result[count++] = '\\';
        }

        return new string(result, 0, count);
    }
}

internal sealed class CloudShellTraceExporter(HttpClient httpClient, IHostEnvironment environment) :
    BaseExporter<Activity>
{
    private readonly string serviceName =
        SignalRObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly string resourceId =
        SignalRObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("CLOUDSHELL_TELEMETRY_RESOURCE_ID"),
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID"),
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly Uri? endpoint = SignalRObservability.TryCreateEndpoint(
        Environment.GetEnvironmentVariable("CLOUDSHELL_TRACE_INGEST_ENDPOINT"));
    private readonly IReadOnlyDictionary<string, string> resourceAttributes =
        SignalRObservability.ParseOtelResourceAttributes(
            Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES"));

    public override ExportResult Export(in Batch<Activity> batch)
    {
        if (endpoint is null)
        {
            return ExportResult.Success;
        }

        var spans = new List<CloudShellTraceSpan>();
        foreach (var activity in batch)
        {
            if (activity.TraceId != default && activity.SpanId != default)
            {
                spans.Add(CreateSpan(activity));
            }
        }

        if (spans.Count == 0)
        {
            return ExportResult.Success;
        }

        try
        {
            using var scope = SuppressInstrumentationScope.Begin();
            var response = httpClient
                .PostAsJsonAsync(endpoint, new CloudShellTraceIngestRequest(spans))
                .GetAwaiter()
                .GetResult();
            return response.IsSuccessStatusCode
                ? ExportResult.Success
                : ExportResult.Failure;
        }
        catch
        {
            return ExportResult.Failure;
        }
    }

    private CloudShellTraceSpan CreateSpan(Activity activity)
    {
        var attributes = new Dictionary<string, string>(resourceAttributes, StringComparer.OrdinalIgnoreCase);
        foreach (var tag in activity.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag.Key) && tag.Value is not null)
            {
                attributes[tag.Key] = tag.Value;
            }
        }

        return new(
            activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(),
            activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
            activity.DisplayName,
            resourceId,
            serviceName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            new DateTimeOffset(activity.StartTimeUtc, TimeSpan.Zero),
            activity.Duration,
            attributes);
    }
}

internal sealed record CloudShellTraceIngestRequest(IReadOnlyList<CloudShellTraceSpan> Spans);

internal sealed record CloudShellMetricIngestRequest(IReadOnlyList<CloudShellMetricPoint> Points);

internal sealed record CloudShellTraceSpan(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    string ResourceId,
    string ServiceName,
    string Kind,
    string Status,
    DateTimeOffset StartTime,
    TimeSpan Duration,
    IReadOnlyDictionary<string, string> Attributes);

internal sealed record CloudShellMetricPoint(
    string Name,
    string ResourceId,
    string ServiceName,
    double Value,
    DateTimeOffset Timestamp,
    string? Unit,
    IReadOnlyDictionary<string, string> Attributes);
