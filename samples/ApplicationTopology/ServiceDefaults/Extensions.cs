using CloudShell.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;

namespace CloudShell.ApplicationTopology.ServiceDefaults;

public static class Extensions
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
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

        builder.Services.AddHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient<CloudShellTraceExporter>();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddServiceDiscovery();
        });
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ApplicationTopologyTraceSources.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddProcessor(serviceProvider =>
                        new BatchActivityExportProcessor(
                            serviceProvider.GetRequiredService<CloudShellTraceExporter>()));
            });

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseCloudShellMetrics();

        app.MapHealthChecks("/healthz");
        app.MapGet("/alive", () => Results.Ok(new
        {
            status = "alive",
            service = app.Environment.ApplicationName,
            timestamp = DateTimeOffset.UtcNow
        }));

        return app;
    }

    private static WebApplication UseCloudShellMetrics(this WebApplication app)
    {
        var endpoint = ApplicationTopologyObservability.TryCreateEndpoint(
            Environment.GetEnvironmentVariable("CLOUDSHELL_METRIC_INGEST_ENDPOINT"));
        if (endpoint is null)
        {
            return app;
        }

        var serviceName = ApplicationTopologyObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            app.Environment.ApplicationName);
        var resourceId = ApplicationTopologyObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID"),
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            app.Environment.ApplicationName);

        app.Use(async (context, next) =>
        {
            var stopwatch = Stopwatch.StartNew();
            await next(context);
            stopwatch.Stop();

            var route = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "/";
            var timestamp = DateTimeOffset.UtcNow;
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["http.method"] = context.Request.Method,
                ["http.route"] = route,
                ["http.status_code"] = context.Response.StatusCode.ToString(CultureInfo.InvariantCulture)
            };
            var points = new[]
            {
                new CloudShellMetricPoint(
                    "http.server.requests",
                    resourceId,
                    serviceName,
                    1,
                    timestamp,
                    "count",
                    attributes),
                new CloudShellMetricPoint(
                    "http.server.duration",
                    resourceId,
                    serviceName,
                    stopwatch.Elapsed.TotalMilliseconds,
                    timestamp,
                    "ms",
                    attributes)
            };

            _ = Task.Run(() => ApplicationTopologyObservability.PostMetricPointsAsync(endpoint, points));
        });

        return app;
    }

    public static IHttpClientBuilder AddResourceHttpClient(
        this IServiceCollection services,
        string name,
        string resourceName,
        string endpointName) =>
        services.AddHttpClient(name, client =>
        {
            var host = string.Equals(endpointName, "http", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(endpointName, "https", StringComparison.OrdinalIgnoreCase)
                ? resourceName
                : $"_{endpointName}.{resourceName}";
            client.BaseAddress = new Uri($"https+http://{host}");
        });

    public static Uri GetRequiredResourceUri(
        this IConfiguration configuration,
        string resourceName,
        string endpointName) =>
        configuration.GetResourceUri(resourceName, endpointName)
        ?? throw new InvalidOperationException(
            $"Resource endpoint 'services:{resourceName}:{endpointName}' was not found in configuration.");
}

public static class ApplicationTopologyTraceSources
{
    public const string ActivitySourceName = "CloudShell.ApplicationTopology";

    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}

public static class ApplicationTopologyLogEvents
{
    public static readonly EventId CallingApi = new(1000, nameof(CallingApi));

    public static readonly EventId ApiResponseReceived = new(1001, nameof(ApiResponseReceived));

    public static readonly EventId CallingFailingApi = new(1002, nameof(CallingFailingApi));

    public static readonly EventId FailingApiResponseReceived = new(1003, nameof(FailingApiResponseReceived));

    public static readonly EventId FallbackAttemptFailed = new(1004, nameof(FallbackAttemptFailed));

    public static readonly EventId FallbackRecovered = new(1005, nameof(FallbackRecovered));

    public static readonly EventId PreparingMessage = new(2000, nameof(PreparingMessage));

    public static readonly EventId MessagePrepared = new(2001, nameof(MessagePrepared));

    public static readonly EventId CheckingDatabase = new(2100, nameof(CheckingDatabase));

    public static readonly EventId DatabaseChecked = new(2101, nameof(DatabaseChecked));

    public static readonly EventId IntentionalFailureInvoked = new(2200, nameof(IntentionalFailureInvoked));
}

internal static class ApplicationTopologyObservability
{
    private static readonly HttpClient MetricHttpClient = new();

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

    public static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? "unknown-service";

    public static Uri? TryCreateEndpoint(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}

public static class ApplicationTopologyProblemDetails
{
    public static IDictionary<string, object?> CreateFailureExtensions(
        string resourceName,
        int? upstreamStatusCode = null)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resourceName"] = resourceName,
            ["sampleFailureKind"] = "intentional"
        };

        var traceId = GetCurrentTraceId();
        if (traceId is not null)
        {
            extensions["traceId"] = traceId;
        }

        if (upstreamStatusCode is not null)
        {
            extensions["upstreamStatusCode"] = upstreamStatusCode.Value;
        }

        return extensions;
    }

    private static string? GetCurrentTraceId()
    {
        var current = Activity.Current;
        return current is null || current.TraceId == default
            ? null
            : current.TraceId.ToHexString();
    }
}

internal sealed class CloudShellTraceExporter(HttpClient httpClient, IHostEnvironment environment) :
    BaseExporter<Activity>
{
    private readonly string serviceName =
        ApplicationTopologyObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly string resourceId =
        ApplicationTopologyObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID"),
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly Uri? endpoint = ApplicationTopologyObservability.TryCreateEndpoint(
        Environment.GetEnvironmentVariable("CLOUDSHELL_TRACE_INGEST_ENDPOINT"));

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

    private CloudShellTraceSpan CreateSpan(Activity activity) =>
        new(
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
            activity.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Key) && tag.Value is not null)
                .ToDictionary(tag => tag.Key, tag => tag.Value!, StringComparer.OrdinalIgnoreCase));

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
