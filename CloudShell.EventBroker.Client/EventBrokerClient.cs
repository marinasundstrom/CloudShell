using CloudShell.Client.Authentication;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CloudShell.EventBroker.Client;

public sealed class EventBrokerClient
{
    public const string DefaultScope = "ControlPlane.Access";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly CloudShellResourceCredential? credential;
    private readonly IReadOnlyList<string> scopes;

    public EventBrokerClient(
        Uri endpoint,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        Endpoint = endpoint;
        this.httpClient = httpClient ?? new HttpClient();
        scopes = [DefaultScope];
    }

    public EventBrokerClient(
        Uri endpoint,
        CloudShellResourceCredential credential,
        IEnumerable<string>? scopes = null)
        : this(endpoint, credential, new HttpClient(), scopes)
    {
    }

    public EventBrokerClient(
        Uri endpoint,
        CloudShellResourceCredential credential,
        HttpClient httpClient,
        IEnumerable<string>? scopes = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(httpClient);

        Endpoint = endpoint;
        this.credential = credential;
        this.httpClient = httpClient;
        this.scopes = (scopes ?? [DefaultScope]).ToArray();
    }

    public Uri Endpoint { get; }

    public async Task<IReadOnlyList<EventBrokerStreamSummary>> ListStreamsAsync(
        string brokerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerId);

        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            BuildStreamsEndpoint(brokerId),
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var streams = await response.Content.ReadFromJsonAsync<IReadOnlyList<EventBrokerStreamSummary>>(
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

        using var message = await CreateRequestAsync(
            HttpMethod.Post,
            BuildStreamEventsEndpoint(brokerId, stream),
            cancellationToken);
        message.Content = JsonContent.Create(request, options: SerializerOptions);
        using var response = await httpClient.SendAsync(message, cancellationToken);
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

        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            builder.Uri,
            cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var events = await response.Content.ReadFromJsonAsync<EventBrokerEventsResponse>(
            SerializerOptions,
            cancellationToken);
        return events ?? new EventBrokerEventsResponse(stream, fromSequence, []);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, uri);
        if (credential is null)
        {
            return request;
        }

        var token = await credential.GetTokenAsync(
            new CloudShellResourceTokenRequest(scopes),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(token.Token))
        {
            throw new CloudShellAuthenticationException(
                "CloudShell resource credential returned no access token.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
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
