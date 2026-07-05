using System.Net.Http.Json;
using System.Text.Json;

namespace CloudShell.EventBroker.Client;

public sealed class EventBrokerClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    public EventBrokerClient(
        Uri endpoint,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
    }

    public Uri Endpoint { get; }

    public async Task<IReadOnlyList<EventBrokerStreamSummary>> ListStreamsAsync(
        string brokerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerId);

        var streams = await httpClient.GetFromJsonAsync<IReadOnlyList<EventBrokerStreamSummary>>(
            BuildStreamsEndpoint(brokerId),
            SerializerOptions,
            cancellationToken);
        return streams ?? [];
    }

    public async Task<EventBrokerEvent> PublishAsync(
        string brokerId,
        string stream,
        EventBrokerPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ArgumentNullException.ThrowIfNull(request);

        using var response = await httpClient.PostAsJsonAsync(
            BuildStreamEventsEndpoint(brokerId, stream),
            request,
            SerializerOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<EventBrokerEvent>(
            SerializerOptions,
            cancellationToken) ??
            throw new InvalidOperationException("CloudShell Event Broker returned no event.");
    }

    public async Task<EventBrokerEventsResponse> ReadEventsAsync(
        string brokerId,
        string stream,
        long fromSequence = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);

        var endpoint = BuildStreamEventsEndpoint(brokerId, stream);
        var builder = new UriBuilder(endpoint);
        builder.Query = $"fromSequence={Math.Max(0, fromSequence)}&limit={Math.Clamp(limit, 1, 1000)}";

        var events = await httpClient.GetFromJsonAsync<EventBrokerEventsResponse>(
            builder.Uri,
            SerializerOptions,
            cancellationToken);
        return events ?? new EventBrokerEventsResponse(stream, fromSequence, []);
    }

    private Uri BuildStreamsEndpoint(string brokerId) =>
        new($"{Endpoint.ToString().TrimEnd('/')}/api/events/brokers/{Uri.EscapeDataString(brokerId)}/streams");

    private Uri BuildStreamEventsEndpoint(string brokerId, string stream) =>
        new($"{Endpoint.ToString().TrimEnd('/')}/api/events/brokers/{Uri.EscapeDataString(brokerId)}/streams/{Uri.EscapeDataString(stream)}/events");

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail)
                ? $"CloudShell Event Broker returned {(int)response.StatusCode}."
                : $"CloudShell Event Broker returned {(int)response.StatusCode}. {detail}",
            null,
            response.StatusCode);
    }
}
