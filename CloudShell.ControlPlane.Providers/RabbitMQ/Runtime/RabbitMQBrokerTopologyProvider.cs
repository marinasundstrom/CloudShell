using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public interface IRabbitMQBrokerTopologyProvider
{
    Task<RabbitMQBrokerTopologyResult> GetTopologyAsync(
        ResourceManagerResource resource,
        CancellationToken cancellationToken = default);
}

public sealed record RabbitMQBrokerTopologyResult(
    string VirtualHost,
    IReadOnlyList<RabbitMQQueueInfo> Queues,
    IReadOnlyList<RabbitMQExchangeInfo> Exchanges,
    IReadOnlyList<RabbitMQBindingInfo> Bindings,
    DateTimeOffset ObservedAt,
    string? ErrorMessage = null)
{
    public bool Succeeded => string.IsNullOrWhiteSpace(ErrorMessage);

    public static RabbitMQBrokerTopologyResult Unavailable(
        string virtualHost,
        string errorMessage) =>
        new(virtualHost, [], [], [], DateTimeOffset.UtcNow, errorMessage);
}

public sealed record RabbitMQQueueInfo(
    string Name,
    string VirtualHost,
    bool Durable,
    bool AutoDelete,
    bool Exclusive,
    string? State,
    int? Messages,
    int? Consumers,
    string? Type);

public sealed record RabbitMQExchangeInfo(
    string Name,
    string VirtualHost,
    string Type,
    bool Durable,
    bool AutoDelete,
    bool Internal);

public sealed record RabbitMQBindingInfo(
    string Source,
    string VirtualHost,
    string Destination,
    string DestinationType,
    string RoutingKey,
    string PropertiesKey);

public sealed class NoopRabbitMQBrokerTopologyProvider(
    IOptions<RabbitMQManagementAccessOptions>? options = null) :
    IRabbitMQBrokerTopologyProvider
{
    private readonly RabbitMQManagementAccessOptions options =
        options?.Value ?? new RabbitMQManagementAccessOptions();

    public Task<RabbitMQBrokerTopologyResult> GetTopologyAsync(
        ResourceManagerResource resource,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RabbitMQBrokerTopologyResult.Unavailable(
            ResolveVirtualHost(resource, options),
            "RabbitMQ broker topology requires a RabbitMQ Management API provider."));

    internal static string ResolveVirtualHost(
        ResourceManagerResource resource,
        RabbitMQManagementAccessOptions options) =>
        RabbitMQResourceConfiguration.ResolveVirtualHost(resource, options);
}

public sealed class RabbitMQManagementApiBrokerTopologyProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<RabbitMQManagementAccessOptions> options,
    IRabbitMQBootstrapCredentialProvider bootstrapCredentials) :
    IRabbitMQBrokerTopologyProvider
{
    private readonly RabbitMQManagementAccessOptions options = options.Value;

    public async Task<RabbitMQBrokerTopologyResult> GetTopologyAsync(
        ResourceManagerResource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var virtualHost = NoopRabbitMQBrokerTopologyProvider.ResolveVirtualHost(resource, options);
        if (!RabbitMQManagementApiHttp.TryGetResourceManagerManagementUri(
                resource,
                out var managementUri))
        {
            return RabbitMQBrokerTopologyResult.Unavailable(
                virtualHost,
                "RabbitMQ broker topology requires a resolved management endpoint.");
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
            var queues = await ReadAsync<RabbitMQQueueResponse>(
                client,
                $"api/queues/{encodedVirtualHost}?disable_stats=true",
                "read RabbitMQ queues",
                cancellationToken);
            var exchanges = await ReadAsync<RabbitMQExchangeResponse>(
                client,
                $"api/exchanges/{encodedVirtualHost}?disable_stats=true",
                "read RabbitMQ exchanges",
                cancellationToken);
            var bindings = await ReadAsync<RabbitMQBindingResponse>(
                client,
                $"api/bindings/{encodedVirtualHost}",
                "read RabbitMQ bindings",
                cancellationToken);

            return new RabbitMQBrokerTopologyResult(
                virtualHost,
                queues
                    .Select(queue => queue.ToInfo(virtualHost))
                    .OrderBy(queue => queue.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                exchanges
                    .Select(exchange => exchange.ToInfo(virtualHost))
                    .OrderBy(exchange => string.IsNullOrEmpty(exchange.Name) ? 0 : 1)
                    .ThenBy(exchange => exchange.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                bindings
                    .Select(binding => binding.ToInfo(virtualHost))
                    .OrderBy(binding => binding.Source, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(binding => binding.DestinationType, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(binding => binding.Destination, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(binding => binding.RoutingKey, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return RabbitMQBrokerTopologyResult.Unavailable(virtualHost, exception.Message);
        }
    }

    private static async Task<IReadOnlyList<T>> ReadAsync<T>(
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

    private sealed record RabbitMQQueueResponse(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("vhost")] string? VirtualHost,
        [property: JsonPropertyName("durable")] bool Durable,
        [property: JsonPropertyName("auto_delete")] bool AutoDelete,
        [property: JsonPropertyName("exclusive")] bool Exclusive,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("messages")] int? Messages,
        [property: JsonPropertyName("consumers")] int? Consumers,
        [property: JsonPropertyName("type")] string? Type)
    {
        public RabbitMQQueueInfo ToInfo(string fallbackVirtualHost) =>
            new(
                Name ?? string.Empty,
                string.IsNullOrWhiteSpace(VirtualHost) ? fallbackVirtualHost : VirtualHost!,
                Durable,
                AutoDelete,
                Exclusive,
                State,
                Messages,
                Consumers,
                Type);
    }

    private sealed record RabbitMQExchangeResponse(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("vhost")] string? VirtualHost,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("durable")] bool Durable,
        [property: JsonPropertyName("auto_delete")] bool AutoDelete,
        [property: JsonPropertyName("internal")] bool Internal)
    {
        public RabbitMQExchangeInfo ToInfo(string fallbackVirtualHost) =>
            new(
                Name ?? string.Empty,
                string.IsNullOrWhiteSpace(VirtualHost) ? fallbackVirtualHost : VirtualHost!,
                string.IsNullOrWhiteSpace(Type) ? "unknown" : Type!,
                Durable,
                AutoDelete,
                Internal);
    }

    private sealed record RabbitMQBindingResponse(
        [property: JsonPropertyName("source")] string? Source,
        [property: JsonPropertyName("vhost")] string? VirtualHost,
        [property: JsonPropertyName("destination")] string? Destination,
        [property: JsonPropertyName("destination_type")] string? DestinationType,
        [property: JsonPropertyName("routing_key")] string? RoutingKey,
        [property: JsonPropertyName("properties_key")] string? PropertiesKey)
    {
        public RabbitMQBindingInfo ToInfo(string fallbackVirtualHost) =>
            new(
                Source ?? string.Empty,
                string.IsNullOrWhiteSpace(VirtualHost) ? fallbackVirtualHost : VirtualHost!,
                Destination ?? string.Empty,
                string.IsNullOrWhiteSpace(DestinationType) ? "unknown" : DestinationType!,
                RoutingKey ?? string.Empty,
                PropertiesKey ?? string.Empty);
    }
}
