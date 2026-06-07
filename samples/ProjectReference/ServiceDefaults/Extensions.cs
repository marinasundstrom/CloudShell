using CloudShell.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Net.Http.Json;

namespace CloudShell.ProjectReference.ServiceDefaults;

public static class Extensions
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
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
        app.MapHealthChecks("/healthz");
        app.MapGet("/alive", () => Results.Ok(new
        {
            status = "alive",
            service = app.Environment.ApplicationName,
            timestamp = DateTimeOffset.UtcNow
        }));

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

internal sealed class CloudShellTraceExporter(HttpClient httpClient, IHostEnvironment environment) :
    BaseExporter<Activity>
{
    private readonly string serviceName =
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly string resourceId =
        FirstNonEmpty(
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID"),
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
            environment.ApplicationName);
    private readonly Uri? endpoint = TryCreateEndpoint(
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

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? "unknown-service";

    private static Uri? TryCreateEndpoint(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}

internal sealed record CloudShellTraceIngestRequest(IReadOnlyList<CloudShellTraceSpan> Spans);

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
