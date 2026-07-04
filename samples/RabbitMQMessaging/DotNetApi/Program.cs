using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CloudShell.RabbitMQ.Client;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient<CloudShellTraceExporter>();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(RabbitMqTraceSources.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddProcessor(serviceProvider =>
                new BatchActivityExportProcessor(
                    serviceProvider.GetRequiredService<CloudShellTraceExporter>()));
    });

var options = RabbitMqOptions.FromConfiguration(builder.Configuration);
if (options.UseCloudShellCredentials)
{
    builder.Services.AddCloudShellRabbitMQClient(client =>
    {
        client.RabbitMQResourceName = options.ResourceName;
        client.HostName = options.Host;
        client.Port = options.Port;
        client.Permission = options.CredentialPermission;
    });
}

var messages = new MessageStore();
var app = builder.Build();
var broker = await RabbitMqBroker.ConnectAsync(options, messages, app.Services);

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
        MessageStore messages,
        IServiceProvider services)
    {
        var factory = options.UseCloudShellCredentials
            ? await services
                .GetRequiredService<CloudShellRabbitMQConnectionFactory>()
                .CreateConnectionFactoryAsync()
            : new ConnectionFactory
            {
                HostName = options.Host,
                Port = options.Port,
                UserName = options.Username ?? "guest",
                Password = options.Password ?? "guest",
                VirtualHost = options.VirtualHost ?? "/"
            };

        factory.DispatchConsumersAsync = false;

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
        using var activity = RabbitMqTraceSources.ActivitySource.StartActivity(
            "rabbitmq publish",
            ActivityKind.Producer);
        var envelope = CreateMessageEnvelope(message, subject);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation", "publish");
        activity?.SetTag("messaging.destination.name", options.Exchange);
        activity?.SetTag("messaging.message.id", envelope.Id);
        activity?.SetTag("messaging.message.conversation_id", envelope.Subject);

        try
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, MessageJson.Options));
            var properties = publishChannel.CreateBasicProperties();
            InjectTraceContext(properties);

            lock (publishGate)
            {
                publishChannel.BasicPublish(
                    exchange: options.Exchange,
                    routingKey: string.Empty,
                    basicProperties: properties,
                    body: body);
            }

            return envelope;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
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
            var parentContext = ExtractTraceContext(delivery.BasicProperties);
            using var activity = RabbitMqTraceSources.ActivitySource.StartActivity(
                "rabbitmq consume",
                ActivityKind.Consumer,
                parentContext.ActivityContext);
            try
            {
                var json = Encoding.UTF8.GetString(delivery.Body.ToArray());
                var envelope = JsonSerializer.Deserialize<MessageEnvelope>(json, MessageJson.Options);
                activity?.SetTag("messaging.system", "rabbitmq");
                activity?.SetTag("messaging.operation", "consume");
                activity?.SetTag("messaging.destination.name", options.Queue);
                if (envelope is not null)
                {
                    activity?.SetTag("messaging.message.id", envelope.Id);
                    activity?.SetTag("messaging.message.conversation_id", envelope.Subject);
                    activity?.SetTag("messaging.message.origin", envelope.Origin);
                    messages.Add(envelope);
                }
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                throw;
            }
        };
        consumeChannel.BasicConsume(options.Queue, autoAck: true, consumer);
    }

    private static MessageEnvelope CreateMessageEnvelope(
        string message,
        string? subject) =>
        new(
            Id: Guid.NewGuid().ToString("N"),
            Origin: "dotnet",
            Subject: string.IsNullOrWhiteSpace(subject) ? "sample.event" : subject,
            Message: message,
            Timestamp: DateTimeOffset.UtcNow);

    private static void InjectTraceContext(IBasicProperties properties)
    {
        if (Activity.Current is null)
        {
            return;
        }

        properties.Headers ??= new Dictionary<string, object>();
        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(Activity.Current.Context, Baggage.Current),
            properties,
            static (carrier, key, value) => carrier.Headers![key] = value);
    }

    private static PropagationContext ExtractTraceContext(IBasicProperties properties) =>
        Propagators.DefaultTextMapPropagator.Extract(
            default,
            properties,
            static (carrier, key) =>
            {
                if (carrier.Headers is null ||
                    !carrier.Headers.TryGetValue(key, out var value) ||
                    value is null)
                {
                    return [];
                }

                return [HeaderValueToString(value)];
            });

    private static string HeaderValueToString(object value) =>
        value switch
        {
            string text => text,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            ReadOnlyMemory<byte> bytes => Encoding.UTF8.GetString(bytes.Span),
            _ => value.ToString() ?? string.Empty
        };

    public void Dispose()
    {
        consumeChannel.Dispose();
        publishChannel.Dispose();
        connection.Dispose();
    }
}

internal static class RabbitMqTraceSources
{
    public const string ActivitySourceName = "CloudShell.RabbitMQMessaging";

    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}

internal sealed class CloudShellTraceExporter(HttpClient httpClient, IHostEnvironment environment) :
    BaseExporter<Activity>
{
    private readonly string serviceName = FirstNonEmpty(
        Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME"),
        environment.ApplicationName);
    private readonly string resourceId = FirstNonEmpty(
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
    bool UseCloudShellCredentials,
    string? Username,
    string? Password,
    string? VirtualHost,
    string ResourceName,
    string CredentialPermission,
    string Exchange,
    string Queue)
{
    public static RabbitMqOptions FromConfiguration(
        IConfiguration configuration)
    {
        var authentication = configuration["RabbitMQ:Authentication"];
        var requiresCloudShellCredentials =
            string.Equals(authentication, "CloudShell", StringComparison.OrdinalIgnoreCase);

        return new(
            configuration["RabbitMQ:Host"] ?? "localhost",
            int.TryParse(configuration["RabbitMQ:Port"], out var port) ? port : 5672,
            requiresCloudShellCredentials,
            configuration["RabbitMQ:Username"],
            configuration["RabbitMQ:Password"],
            configuration["RabbitMQ:VirtualHost"],
            configuration["RabbitMQ:ResourceName"] ?? "rabbitmq",
            configuration["RabbitMQ:CredentialPermission"] ?? CloudShellRabbitMQPermissions.Configure,
            configuration["RabbitMQ:Exchange"] ?? "cloudshell.sample.events",
            configuration["RabbitMQ:Queue"] ?? "rabbitmq-dotnet-events");
    }
}
