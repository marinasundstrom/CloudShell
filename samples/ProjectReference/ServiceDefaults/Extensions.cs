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

namespace CloudShell.ProjectReference.ServiceDefaults;

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
                    .AddSource(ProjectReferenceTraceSources.ActivitySourceName)
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
        var endpoint = ProjectReferenceObservability.TryCreateEndpoint(
            Environment.GetEnvironmentVariable("CLOUDSHELL_METRIC_INGEST_ENDPOINT"));
        if (endpoint is null)
        {
            return app;
        }

        var serviceName = ProjectReferenceObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            app.Environment.ApplicationName);
        var resourceId = ProjectReferenceObservability.FirstNonEmpty(
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

            _ = Task.Run(() => ProjectReferenceObservability.PostMetricPointsAsync(endpoint, points));
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

public static class ProjectReferenceTraceSources
{
    public const string ActivitySourceName = "CloudShell.ProjectReference";

    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}

public static class ProjectReferenceLogEvents
{
    public static readonly EventId CallingApi = new(1000, nameof(CallingApi));

    public static readonly EventId ApiResponseReceived = new(1001, nameof(ApiResponseReceived));

    public static readonly EventId PreparingMessage = new(2000, nameof(PreparingMessage));

    public static readonly EventId MessagePrepared = new(2001, nameof(MessagePrepared));
}

internal static class ProjectReferenceObservability
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

internal sealed class CloudShellTraceExporter(HttpClient httpClient, IHostEnvironment environment) :
    BaseExporter<Activity>
{
    private readonly string serviceName =
        ProjectReferenceObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly string resourceId =
        ProjectReferenceObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID"),
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly Uri? endpoint = ProjectReferenceObservability.TryCreateEndpoint(
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
