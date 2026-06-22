using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;

namespace CloudShell.ReplicatedContainerHealth.ServiceDefaults;

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

        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient<CloudShellTraceExporter>();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ReplicatedContainerHealthTraceSources.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddProcessor(serviceProvider =>
                        new BatchActivityExportProcessor(
                            serviceProvider.GetRequiredService<CloudShellTraceExporter>()));
            });

        return builder;
    }

    public static WebApplication UseCloudShellMetrics(this WebApplication app)
    {
        var endpoint = ReplicatedContainerHealthObservability.TryCreateEndpoint(
            Environment.GetEnvironmentVariable("CLOUDSHELL_METRIC_INGEST_ENDPOINT"));
        if (endpoint is null)
        {
            return app;
        }

        var serviceName = ReplicatedContainerHealthObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            app.Environment.ApplicationName);
        var resourceId = ReplicatedContainerHealthObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID"),
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            app.Environment.ApplicationName);
        var resourceAttributes = ReplicatedContainerHealthObservability.ParseOtelResourceAttributes(
            Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES"));

        app.Use(async (context, next) =>
        {
            var stopwatch = Stopwatch.StartNew();
            await next(context);
            stopwatch.Stop();

            var route = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "/";
            var timestamp = DateTimeOffset.UtcNow;
            var attributes = new Dictionary<string, string>(resourceAttributes, StringComparer.OrdinalIgnoreCase)
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

            _ = Task.Run(() => ReplicatedContainerHealthObservability.PostMetricPointsAsync(endpoint, points));
        });

        return app;
    }
}

public static class ReplicatedContainerHealthTraceSources
{
    public const string ActivitySourceName = "CloudShell.ReplicatedContainerHealth";

    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}

public static class ReplicatedContainerHealthLogEvents
{
    public static readonly EventId HealthChecked = new(1000, nameof(HealthChecked));

    public static readonly EventId LivenessChecked = new(1001, nameof(LivenessChecked));

    public static readonly EventId DemoWorkHandled = new(1002, nameof(DemoWorkHandled));
}

internal static class ReplicatedContainerHealthObservability
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
        ReplicatedContainerHealthObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly string resourceId =
        ReplicatedContainerHealthObservability.FirstNonEmpty(
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID"),
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly Uri? endpoint = ReplicatedContainerHealthObservability.TryCreateEndpoint(
        Environment.GetEnvironmentVariable("CLOUDSHELL_TRACE_INGEST_ENDPOINT"));
    private readonly IReadOnlyDictionary<string, string> resourceAttributes =
        ReplicatedContainerHealthObservability.ParseOtelResourceAttributes(
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
