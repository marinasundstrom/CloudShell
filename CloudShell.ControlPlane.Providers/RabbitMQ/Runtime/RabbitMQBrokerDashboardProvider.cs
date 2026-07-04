using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public interface IRabbitMQBrokerDashboardProvider
{
    Task<RabbitMQBrokerDashboardResult> GetDashboardAsync(
        ResourceManagerResource resource,
        CancellationToken cancellationToken = default);
}

public sealed record RabbitMQBrokerDashboardResult(
    string VirtualHost,
    RabbitMQBrokerDashboardTotals Totals,
    IReadOnlyList<RabbitMQBrokerThroughputSample> Throughput,
    IReadOnlyList<RabbitMQQueueDashboardInfo> Queues,
    DateTimeOffset ObservedAt,
    string? ErrorMessage = null)
{
    public bool Succeeded => string.IsNullOrWhiteSpace(ErrorMessage);

    public static RabbitMQBrokerDashboardResult Unavailable(
        string virtualHost,
        string errorMessage) =>
        new(virtualHost, RabbitMQBrokerDashboardTotals.Empty, [], [], DateTimeOffset.UtcNow, errorMessage);
}

public sealed record RabbitMQBrokerDashboardTotals(
    int? Queues,
    int? Exchanges,
    int? Connections,
    int? Channels,
    int? Consumers,
    long? Messages,
    long? MessagesReady,
    long? MessagesUnacknowledged,
    double? PublishRate,
    double? DeliverGetRate,
    double? AckRate)
{
    public static RabbitMQBrokerDashboardTotals Empty { get; } =
        new(null, null, null, null, null, null, null, null, null, null, null);
}

public sealed record RabbitMQQueueDashboardInfo(
    string Name,
    string VirtualHost,
    string? State,
    long? Messages,
    long? MessagesReady,
    long? MessagesUnacknowledged,
    int? Consumers,
    double? IncomingRate,
    double? DeliverGetRate,
    double? AckRate);

public sealed record RabbitMQBrokerThroughputSample(
    DateTimeOffset Timestamp,
    double? PublishRate,
    double? DeliverGetRate,
    double? AckRate);

public sealed class NoopRabbitMQBrokerDashboardProvider(
    IOptions<RabbitMQManagementAccessOptions>? options = null) :
    IRabbitMQBrokerDashboardProvider
{
    private readonly RabbitMQManagementAccessOptions options =
        options?.Value ?? new RabbitMQManagementAccessOptions();

    public Task<RabbitMQBrokerDashboardResult> GetDashboardAsync(
        ResourceManagerResource resource,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RabbitMQBrokerDashboardResult.Unavailable(
            RabbitMQResourceConfiguration.ResolveVirtualHost(resource, options),
            "RabbitMQ broker dashboard requires a RabbitMQ Management API provider."));
}

public sealed class RabbitMQManagementApiBrokerDashboardProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<RabbitMQManagementAccessOptions> options,
    IRabbitMQBootstrapCredentialProvider bootstrapCredentials) :
    IRabbitMQBrokerDashboardProvider
{
    private readonly RabbitMQManagementAccessOptions options = options.Value;

    public async Task<RabbitMQBrokerDashboardResult> GetDashboardAsync(
        ResourceManagerResource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var virtualHost = RabbitMQResourceConfiguration.ResolveVirtualHost(resource, options);
        if (!RabbitMQManagementApiHttp.TryGetResourceManagerManagementUri(
                resource,
                out var managementUri))
        {
            return RabbitMQBrokerDashboardResult.Unavailable(
                virtualHost,
                "RabbitMQ broker dashboard requires a resolved management endpoint.");
        }

        var client = httpClientFactory.CreateClient(RabbitMQManagementApiAccessReconciler.HttpClientName);
        client.BaseAddress = managementUri;
        client.DefaultRequestHeaders.Authorization =
            RabbitMQManagementApiHttp.CreateAuthorizationHeader(
                bootstrapCredentials.ResolveManagementCredentials(
                    resource,
                    configuration,
                    options));

        try
        {
            var encodedVirtualHost = Uri.EscapeDataString(virtualHost);
            var overview = await ReadSingleAsync<RabbitMQOverviewResponse>(
                client,
                "api/overview?msg_rates_age=300&msg_rates_incr=10",
                "read RabbitMQ overview",
                cancellationToken);
            var queues = await ReadListAsync<RabbitMQQueueDashboardResponse>(
                client,
                $"api/queues/{encodedVirtualHost}",
                "read RabbitMQ queues",
                cancellationToken);
            var queueInfos = queues
                .Select(queue => queue.ToInfo(virtualHost))
                .OrderByDescending(queue => queue.Messages ?? 0)
                .ThenBy(queue => queue.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new RabbitMQBrokerDashboardResult(
                virtualHost,
                overview?.ToTotals(queueInfos) ?? RabbitMQOverviewResponse.CreateFallbackTotals(queueInfos),
                overview?.CreateThroughput() ?? [],
                queueInfos,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidOperationException or JsonException or NotSupportedException)
        {
            return RabbitMQBrokerDashboardResult.Unavailable(virtualHost, exception.Message);
        }
    }

    private static async Task<T?> ReadSingleAsync<T>(
        HttpClient client,
        string requestUri,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(requestUri, cancellationToken);
        await RabbitMQManagementApiHttp.EnsureSuccessAsync(
            response,
            operation,
            cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(
            cancellationToken: cancellationToken);
    }

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(
        HttpClient client,
        string requestUri,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(requestUri, cancellationToken);
        await RabbitMQManagementApiHttp.EnsureSuccessAsync(
            response,
            operation,
            cancellationToken);
        return await response.Content.ReadFromJsonAsync<T[]>(
                cancellationToken: cancellationToken) ??
            [];
    }

    private sealed record RabbitMQOverviewResponse(
        [property: JsonPropertyName("queue_totals")] RabbitMQQueueTotalsResponse? QueueTotals,
        [property: JsonPropertyName("object_totals")] RabbitMQObjectTotalsResponse? ObjectTotals,
        [property: JsonPropertyName("message_stats")] RabbitMQMessageStatsResponse? MessageStats)
    {
        public RabbitMQBrokerDashboardTotals ToTotals(
            IReadOnlyList<RabbitMQQueueDashboardInfo> queues)
        {
            var fallback = CreateFallbackTotals(queues);
            return new RabbitMQBrokerDashboardTotals(
                ObjectTotals?.Queues ?? fallback.Queues,
                ObjectTotals?.Exchanges,
                ObjectTotals?.Connections,
                ObjectTotals?.Channels,
                ObjectTotals?.Consumers ?? fallback.Consumers,
                QueueTotals?.Messages ?? fallback.Messages,
                QueueTotals?.MessagesReady ?? fallback.MessagesReady,
                QueueTotals?.MessagesUnacknowledged ?? fallback.MessagesUnacknowledged,
                MessageStats?.PublishDetails?.Rate,
                MessageStats?.DeliverGetDetails?.Rate,
                MessageStats?.AckDetails?.Rate);
        }

        public static RabbitMQBrokerDashboardTotals CreateFallbackTotals(
            IReadOnlyList<RabbitMQQueueDashboardInfo> queues) =>
            new(
                queues.Count,
                null,
                null,
                null,
                queues.Sum(queue => queue.Consumers ?? 0),
                queues.Sum(queue => queue.Messages ?? 0),
                queues.Sum(queue => queue.MessagesReady ?? 0),
                queues.Sum(queue => queue.MessagesUnacknowledged ?? 0),
                queues.Sum(queue => queue.IncomingRate ?? 0),
                queues.Sum(queue => queue.DeliverGetRate ?? 0),
                queues.Sum(queue => queue.AckRate ?? 0));

        public IReadOnlyList<RabbitMQBrokerThroughputSample> CreateThroughput() =>
            CreateThroughput(MessageStats);

        private static IReadOnlyList<RabbitMQBrokerThroughputSample> CreateThroughput(
            RabbitMQMessageStatsResponse? messageStats)
        {
            if (messageStats is null)
            {
                return [];
            }

            var samples = new SortedDictionary<long, RabbitMQBrokerThroughputSampleBuilder>();
            AddRates(samples, messageStats.PublishDetails, (sample, rate) => sample.PublishRate = rate);
            AddRates(samples, messageStats.DeliverGetDetails, (sample, rate) => sample.DeliverGetRate = rate);
            AddRates(samples, messageStats.AckDetails, (sample, rate) => sample.AckRate = rate);

            if (samples.Count == 0)
            {
                var now = DateTimeOffset.UtcNow;
                return
                [
                    new RabbitMQBrokerThroughputSample(
                        now,
                        messageStats.PublishDetails?.Rate,
                        messageStats.DeliverGetDetails?.Rate,
                        messageStats.AckDetails?.Rate)
                ];
            }

            return samples.Values
                .Select(sample => sample.ToSample())
                .ToArray();
        }

        private static void AddRates(
            SortedDictionary<long, RabbitMQBrokerThroughputSampleBuilder> target,
            RabbitMQRateDetailsResponse? details,
            Action<RabbitMQBrokerThroughputSampleBuilder, double> assignRate)
        {
            if (details?.Samples is not { Count: > 1 } samples)
            {
                return;
            }

            var orderedSamples = samples
                .Where(sample => sample.Timestamp.HasValue && sample.Sample.HasValue)
                .OrderBy(sample => sample.Timestamp!.Value)
                .ToArray();

            for (var index = 1; index < orderedSamples.Length; index++)
            {
                var previous = orderedSamples[index - 1];
                var current = orderedSamples[index];
                var seconds = (current.Timestamp!.Value - previous.Timestamp!.Value) / 1000D;
                if (seconds <= 0)
                {
                    continue;
                }

                var rate = Math.Max((current.Sample!.Value - previous.Sample!.Value) / seconds, 0);
                var builder = GetOrAdd(target, current.Timestamp.Value);
                assignRate(builder, rate);
            }
        }

        private static RabbitMQBrokerThroughputSampleBuilder GetOrAdd(
            SortedDictionary<long, RabbitMQBrokerThroughputSampleBuilder> target,
            long timestamp)
        {
            if (!target.TryGetValue(timestamp, out var sample))
            {
                sample = new RabbitMQBrokerThroughputSampleBuilder(timestamp);
                target[timestamp] = sample;
            }

            return sample;
        }
    }

    private sealed record RabbitMQQueueTotalsResponse(
        [property: JsonPropertyName("messages")] long? Messages,
        [property: JsonPropertyName("messages_ready")] long? MessagesReady,
        [property: JsonPropertyName("messages_unacknowledged")] long? MessagesUnacknowledged);

    private sealed record RabbitMQObjectTotalsResponse(
        [property: JsonPropertyName("channels")] int? Channels,
        [property: JsonPropertyName("connections")] int? Connections,
        [property: JsonPropertyName("consumers")] int? Consumers,
        [property: JsonPropertyName("exchanges")] int? Exchanges,
        [property: JsonPropertyName("queues")] int? Queues);

    private sealed record RabbitMQQueueDashboardResponse(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("vhost")] string? VirtualHost,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("messages")] long? Messages,
        [property: JsonPropertyName("messages_ready")] long? MessagesReady,
        [property: JsonPropertyName("messages_unacknowledged")] long? MessagesUnacknowledged,
        [property: JsonPropertyName("consumers")] int? Consumers,
        [property: JsonPropertyName("message_stats")] RabbitMQMessageStatsResponse? MessageStats)
    {
        public RabbitMQQueueDashboardInfo ToInfo(string fallbackVirtualHost) =>
            new(
                Name ?? string.Empty,
                string.IsNullOrWhiteSpace(VirtualHost) ? fallbackVirtualHost : VirtualHost!,
                State,
                Messages,
                MessagesReady,
                MessagesUnacknowledged,
                Consumers,
                MessageStats?.PublishDetails?.Rate,
                MessageStats?.DeliverGetDetails?.Rate,
                MessageStats?.AckDetails?.Rate);
    }

    private sealed record RabbitMQMessageStatsResponse(
        [property: JsonPropertyName("publish_details")] RabbitMQRateDetailsResponse? PublishDetails,
        [property: JsonPropertyName("deliver_get_details")] RabbitMQRateDetailsResponse? DeliverGetDetails,
        [property: JsonPropertyName("ack_details")] RabbitMQRateDetailsResponse? AckDetails);

    private sealed record RabbitMQRateDetailsResponse(
        [property: JsonPropertyName("rate")] double? Rate,
        [property: JsonPropertyName("samples")] IReadOnlyList<RabbitMQRateSampleResponse>? Samples);

    private sealed record RabbitMQRateSampleResponse(
        [property: JsonPropertyName("sample")] double? Sample,
        [property: JsonPropertyName("timestamp")] long? Timestamp);

    private sealed class RabbitMQBrokerThroughputSampleBuilder(long timestamp)
    {
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);

        public double? PublishRate { get; set; }

        public double? DeliverGetRate { get; set; }

        public double? AckRate { get; set; }

        public RabbitMQBrokerThroughputSample ToSample() =>
            new(Timestamp, PublishRate, DeliverGetRate, AckRate);
    }
}
