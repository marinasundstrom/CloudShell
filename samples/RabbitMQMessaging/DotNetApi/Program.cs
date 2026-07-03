using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CloudShell.Client.Authentication;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var builder = WebApplication.CreateBuilder(args);
var options = await RabbitMqOptions.FromConfigurationAsync(builder.Configuration);
var messages = new MessageStore();
var broker = await RabbitMqBroker.ConnectAsync(options, messages);
var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(broker.Dispose);

app.MapGet("/", () => Results.Redirect("/messages"));
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "healthy",
    service = "rabbitmq-dotnet",
    broker = $"{options.Host}:{options.Port}"
}));
app.MapGet("/alive", () => Results.Text("alive"));
app.MapGet("/messages", () => Results.Ok(messages.Snapshot()));
app.MapPost("/publish", (PublishRequest request) =>
{
    var envelope = broker.Publish(request.Message, request.Subject);
    return Results.Accepted(value: envelope);
});
app.MapGet("/publish", (string? message, string? subject) =>
{
    var envelope = broker.Publish(
        string.IsNullOrWhiteSpace(message) ? "Hello from the .NET RabbitMQ sample." : message,
        subject);
    return Results.Accepted(value: envelope);
});

app.Run();

internal sealed class RabbitMqBroker : IDisposable
{
    private readonly RabbitMqOptions options;
    private readonly MessageStore messages;
    private readonly IConnection connection;
    private readonly IModel publishChannel;
    private readonly IModel consumeChannel;
    private readonly object publishGate = new();

    private RabbitMqBroker(
        RabbitMqOptions options,
        MessageStore messages,
        IConnection connection)
    {
        this.options = options;
        this.messages = messages;
        this.connection = connection;
        publishChannel = connection.CreateModel();
        consumeChannel = connection.CreateModel();
    }

    public static async Task<RabbitMqBroker> ConnectAsync(
        RabbitMqOptions options,
        MessageStore messages)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.Username,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            DispatchConsumersAsync = false
        };

        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? lastException = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var connection = factory.CreateConnection("cloudshell-rabbitmq-dotnet-sample");
                var broker = new RabbitMqBroker(options, messages, connection);
                broker.ConfigureTopology();
                broker.StartConsumer();
                return broker;
            }
            catch (Exception exception) when (exception is RabbitMQ.Client.Exceptions.BrokerUnreachableException or System.Net.Sockets.SocketException)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException(
            $"RabbitMQ was not reachable at {options.Host}:{options.Port}.",
            lastException);
    }

    public MessageEnvelope Publish(string message, string? subject)
    {
        var envelope = new MessageEnvelope(
            Id: Guid.NewGuid().ToString("N"),
            Origin: "dotnet",
            Subject: string.IsNullOrWhiteSpace(subject) ? "sample.event" : subject,
            Message: message,
            Timestamp: DateTimeOffset.UtcNow);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, MessageJson.Options));

        lock (publishGate)
        {
            publishChannel.BasicPublish(
                exchange: options.Exchange,
                routingKey: string.Empty,
                basicProperties: null,
                body: body);
        }

        return envelope;
    }

    private void ConfigureTopology()
    {
        publishChannel.ExchangeDeclare(options.Exchange, ExchangeType.Fanout, durable: true);
        consumeChannel.ExchangeDeclare(options.Exchange, ExchangeType.Fanout, durable: true);
        consumeChannel.QueueDeclare(options.Queue, durable: false, exclusive: false, autoDelete: false);
        consumeChannel.QueueBind(options.Queue, options.Exchange, routingKey: string.Empty);
    }

    private void StartConsumer()
    {
        var consumer = new EventingBasicConsumer(consumeChannel);
        consumer.Received += (_, delivery) =>
        {
            var json = Encoding.UTF8.GetString(delivery.Body.ToArray());
            var envelope = JsonSerializer.Deserialize<MessageEnvelope>(json, MessageJson.Options);
            if (envelope is not null)
            {
                messages.Add(envelope);
            }
        };
        consumeChannel.BasicConsume(options.Queue, autoAck: true, consumer);
    }

    public void Dispose()
    {
        consumeChannel.Dispose();
        publishChannel.Dispose();
        connection.Dispose();
    }
}

internal sealed class MessageStore
{
    private const int MaxMessages = 100;
    private readonly ConcurrentQueue<MessageEnvelope> messages = new();

    public void Add(MessageEnvelope message)
    {
        messages.Enqueue(message);
        while (messages.Count > MaxMessages)
        {
            messages.TryDequeue(out _);
        }
    }

    public IReadOnlyList<MessageEnvelope> Snapshot() => messages.ToArray();
}

internal sealed record PublishRequest(string Message, string? Subject = null);

internal sealed record MessageEnvelope(
    string Id,
    string Origin,
    string Subject,
    string Message,
    DateTimeOffset Timestamp);

internal static class MessageJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

internal sealed record RabbitMqOptions(
    string Host,
    int Port,
    string Username,
    string Password,
    string VirtualHost,
    string Exchange,
    string Queue)
{
    private const string ConfigurePermission = "CloudShell.Messaging/rabbitMQ/configure/action";

    public static async Task<RabbitMqOptions> FromConfigurationAsync(
        IConfiguration configuration)
    {
        var authentication = configuration["RabbitMQ:Authentication"];
        var requiresCloudShellCredentials =
            string.Equals(authentication, "CloudShell", StringComparison.OrdinalIgnoreCase);
        var username = configuration["RabbitMQ:Username"];
        var password = configuration["RabbitMQ:Password"];
        var virtualHost = configuration["RabbitMQ:VirtualHost"];

        if (requiresCloudShellCredentials)
        {
            var credentials = await ResolveCloudShellCredentialsAsync(configuration);
            username = credentials.Username;
            password = credentials.Password;
            virtualHost = credentials.VirtualHost;
        }

        return new(
            configuration["RabbitMQ:Host"] ?? "localhost",
            int.TryParse(configuration["RabbitMQ:Port"], out var port) ? port : 5672,
            username ?? "guest",
            password ?? "guest",
            virtualHost ?? "/",
            configuration["RabbitMQ:Exchange"] ?? "cloudshell.sample.events",
            configuration["RabbitMQ:Queue"] ?? "rabbitmq-dotnet-events");
    }

    private static async Task<RabbitMqCredentialResponse> ResolveCloudShellCredentialsAsync(
        IConfiguration configuration)
    {
        var endpoint = configuration["RabbitMQ:CredentialEndpoint"];
        var resourceName = configuration["RabbitMQ:ResourceName"] ?? "rabbitmq";
        var permission = configuration["RabbitMQ:CredentialPermission"] ?? ConfigurePermission;
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            throw new InvalidOperationException(
                "RabbitMQ CloudShell authentication requires RabbitMQ:CredentialEndpoint.");
        }

        using var httpClient = new HttpClient();
        var credential = new DefaultCloudShellResourceCredential();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? lastException = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var token = await credential.GetTokenAsync(
                    new CloudShellResourceTokenRequest([permission]));
                using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
                {
                    Content = JsonContent.Create(new RabbitMqCredentialRequest(
                        resourceName,
                        permission))
                };
                request.Headers.Authorization = new("Bearer", token.Token);
                using var response = await httpClient.SendAsync(request);
                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or
                    System.Net.HttpStatusCode.Forbidden)
                {
                    var denied = await response.Content.ReadAsStringAsync();
                    throw new InvalidOperationException(
                        $"CloudShell denied the RabbitMQ credential request: {denied}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    lastException = new InvalidOperationException(
                        $"CloudShell RabbitMQ credential endpoint returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    continue;
                }

                return await response.Content.ReadFromJsonAsync<RabbitMqCredentialResponse>(
                        MessageJson.Options) ??
                    throw new InvalidOperationException(
                        "CloudShell RabbitMQ credential endpoint returned an empty response.");
            }
            catch (Exception exception) when (
                exception is HttpRequestException or
                    TaskCanceledException or
                    CloudShellCredentialUnavailableException or
                    CloudShellAuthenticationException)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new InvalidOperationException(
            "CloudShell RabbitMQ credentials could not be resolved.",
            lastException);
    }
}

internal sealed record RabbitMqCredentialRequest(
    string RabbitMQResourceName,
    string Permission);

internal sealed record RabbitMqCredentialResponse(
    string Username,
    string Password,
    string VirtualHost,
    DateTimeOffset? ExpiresOn = null);
