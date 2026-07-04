package com.example.cloudshell.rabbitmq;

import com.rabbitmq.client.BuiltinExchangeType;
import com.rabbitmq.client.Channel;
import com.rabbitmq.client.Connection;
import com.rabbitmq.client.ConnectionFactory;
import com.rabbitmq.client.DeliverCallback;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.URI;
import java.net.URLDecoder;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.security.SecureRandom;
import java.util.UUID;
import java.util.concurrent.CopyOnWriteArrayList;

public final class RabbitMqSampleServer {
    private RabbitMqSampleServer() {
    }

    public static void main(String[] args) throws Exception {
        RabbitMqOptions options = RabbitMqOptions.fromEnvironment();
        TraceSupport traces = TraceSupport.fromEnvironment();
        MessageStore messages = new MessageStore();
        RabbitMqBroker broker = RabbitMqBroker.connect(options, messages, traces);
        int port = parsePort(System.getenv().getOrDefault("PORT", "5282"), 5282);

        HttpServer server = HttpServer.create(new InetSocketAddress("localhost", port), 0);
        server.createContext("/", exchange -> redirect(exchange, "/messages"));
        server.createContext("/healthz", exchange -> writeJson(exchange, 200, """
            {
              "status": "healthy",
              "service": "rabbitmq-java",
              "broker": "%s:%d"
            }
            """.formatted(escapeJson(options.host()), options.port())));
        server.createContext("/alive", exchange -> writeText(exchange, 200, "alive"));
        server.createContext("/messages", exchange -> writeJson(exchange, 200, messages.toJson()));
        server.createContext("/publish", exchange -> {
            try (TraceSupport.Span span = traces.startHttpServerSpan(exchange)) {
                String message = readMessage(exchange);
                MessageEnvelope envelope = broker.publish(
                    message == null || message.isBlank()
                        ? "Hello from the Java RabbitMQ sample."
                        : message,
                    readQuery(exchange).getOrDefault("subject", "sample.event"),
                    span.context());
                span.attribute("http.status_code", "202");
                writeJson(exchange, 202, envelope.toJson());
            }
        });
        server.start();
        System.out.printf("RabbitMQ Java sample listening on http://localhost:%d%n", port);
    }

    private static String readMessage(HttpExchange exchange) throws IOException {
        Map<String, String> query = readQuery(exchange);
        if (query.containsKey("message")) {
            return query.get("message");
        }

        if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
            return null;
        }

        String body = new String(exchange.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
        return extractJsonString(body, "message").orElse(body);
    }

    private static Map<String, String> readQuery(HttpExchange exchange) {
        String query = exchange.getRequestURI().getRawQuery();
        if (query == null || query.isBlank()) {
            return Map.of();
        }

        java.util.HashMap<String, String> values = new java.util.HashMap<>();
        for (String pair : query.split("&")) {
            int separator = pair.indexOf('=');
            if (separator < 0) {
                values.put(decode(pair), "");
            } else {
                values.put(
                    decode(pair.substring(0, separator)),
                    decode(pair.substring(separator + 1)));
            }
        }

        return values;
    }

    private static java.util.Optional<String> extractJsonString(String json, String property) {
        String marker = "\"" + property + "\"";
        int propertyIndex = json.indexOf(marker);
        if (propertyIndex < 0) {
            return java.util.Optional.empty();
        }

        int colonIndex = json.indexOf(':', propertyIndex + marker.length());
        int valueStart = json.indexOf('"', colonIndex + 1);
        if (colonIndex < 0 || valueStart < 0) {
            return java.util.Optional.empty();
        }

        StringBuilder value = new StringBuilder();
        boolean escaped = false;
        for (int index = valueStart + 1; index < json.length(); index++) {
            char character = json.charAt(index);
            if (escaped) {
                value.append(character);
                escaped = false;
                continue;
            }

            if (character == '\\') {
                escaped = true;
                continue;
            }

            if (character == '"') {
                return java.util.Optional.of(value.toString());
            }

            value.append(character);
        }

        return java.util.Optional.empty();
    }

    private static String decode(String value) {
        return URLDecoder.decode(value, StandardCharsets.UTF_8);
    }

    private static void redirect(HttpExchange exchange, String location) throws IOException {
        exchange.getResponseHeaders().set("Location", location);
        exchange.sendResponseHeaders(302, -1);
        exchange.close();
    }

    private static void writeJson(HttpExchange exchange, int statusCode, String body)
            throws IOException {
        exchange.getResponseHeaders().set("Content-Type", "application/json; charset=utf-8");
        write(exchange, statusCode, body);
    }

    private static void writeText(HttpExchange exchange, int statusCode, String body)
            throws IOException {
        exchange.getResponseHeaders().set("Content-Type", "text/plain; charset=utf-8");
        write(exchange, statusCode, body);
    }

    private static void write(HttpExchange exchange, int statusCode, String body)
            throws IOException {
        byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
        exchange.sendResponseHeaders(statusCode, bytes.length);
        try (OutputStream stream = exchange.getResponseBody()) {
            stream.write(bytes);
        }
    }

    private static int parsePort(String value, int fallback) {
        try {
            return Integer.parseInt(value);
        } catch (NumberFormatException exception) {
            return fallback;
        }
    }

    private static final class TraceSupport {
        private static final SecureRandom RANDOM = new SecureRandom();
        private final HttpClient client;
        private final URI endpoint;
        private final String resourceId;
        private final String serviceName;

        private TraceSupport(
                HttpClient client,
                URI endpoint,
                String resourceId,
                String serviceName) {
            this.client = client;
            this.endpoint = endpoint;
            this.resourceId = resourceId;
            this.serviceName = serviceName;
        }

        static TraceSupport fromEnvironment() {
            String endpointValue = System.getenv("CLOUDSHELL_TRACE_INGEST_ENDPOINT");
            URI endpoint = endpointValue == null || endpointValue.isBlank()
                ? null
                : URI.create(endpointValue);
            String serviceName = firstNonEmpty(
                System.getenv("OTEL_SERVICE_NAME"),
                "rabbitmq-java");
            String resourceId = firstNonEmpty(
                System.getenv("CLOUDSHELL_RESOURCE_ID"),
                serviceName);
            return new TraceSupport(
                HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).build(),
                endpoint,
                resourceId,
                serviceName);
        }

        Span startHttpServerSpan(HttpExchange exchange) {
            TraceContext parent = TraceContext.parse(
                exchange.getRequestHeaders().getFirst("traceparent"));
            Span span = startSpan(
                exchange.getRequestMethod().toUpperCase() + " " + exchange.getRequestURI().getPath(),
                "Server",
                parent);
            span.attribute("http.method", exchange.getRequestMethod());
            span.attribute("http.route", exchange.getRequestURI().getPath());
            return span;
        }

        Span startSpan(String name, String kind, TraceContext parent) {
            TraceContext context = parent == null
                ? new TraceContext(randomHex(16), randomHex(8), null)
                : new TraceContext(parent.traceId(), randomHex(8), parent.spanId());
            return new Span(this, context, name, kind);
        }

        void inject(TraceContext context, Map<String, Object> headers) {
            headers.put("traceparent", context.toTraceParent());
        }

        TraceContext extract(Map<String, Object> headers) {
            if (headers == null) {
                return null;
            }

            Object value = headers.get("traceparent");
            if (value instanceof byte[] bytes) {
                return TraceContext.parse(new String(bytes, StandardCharsets.UTF_8));
            }

            return value == null ? null : TraceContext.parse(value.toString());
        }

        private void export(Span span) {
            if (endpoint == null) {
                return;
            }

            String body = "{\"spans\":[" + span.toJson(resourceId, serviceName) + "]}";
            HttpRequest request = HttpRequest.newBuilder(endpoint)
                .timeout(Duration.ofSeconds(10))
                .header("Content-Type", "application/json")
                .POST(HttpRequest.BodyPublishers.ofString(body))
                .build();
            client.sendAsync(request, HttpResponse.BodyHandlers.discarding());
        }

        private static String firstNonEmpty(String value, String fallback) {
            return value == null || value.isBlank() ? fallback : value.trim();
        }

        private static String randomHex(int bytes) {
            byte[] buffer = new byte[bytes];
            RANDOM.nextBytes(buffer);
            StringBuilder text = new StringBuilder(bytes * 2);
            for (byte item : buffer) {
                text.append(String.format("%02x", item & 0xff));
            }

            return text.toString();
        }

        record TraceContext(String traceId, String spanId, String parentSpanId) {
            static TraceContext parse(String traceparent) {
                if (traceparent == null || traceparent.isBlank()) {
                    return null;
                }

                String[] parts = traceparent.split("-");
                if (parts.length < 4 ||
                    parts[1].length() != 32 ||
                    parts[2].length() != 16) {
                    return null;
                }

                return new TraceContext(parts[1], parts[2], null);
            }

            String toTraceParent() {
                return "00-" + traceId + "-" + spanId + "-01";
            }
        }

        static final class Span implements AutoCloseable {
            private final TraceSupport traces;
            private final TraceContext context;
            private final String name;
            private final String kind;
            private final Instant start = Instant.now();
            private final Map<String, String> attributes = new LinkedHashMap<>();
            private String status = "Unset";

            Span(
                    TraceSupport traces,
                    TraceContext context,
                    String name,
                    String kind) {
                this.traces = traces;
                this.context = context;
                this.name = name;
                this.kind = kind;
            }

            TraceContext context() {
                return context;
            }

            void attribute(String name, String value) {
                if (name != null && !name.isBlank() && value != null) {
                    attributes.put(name, value);
                }
            }

            void error(Exception exception) {
                status = "Error";
                attribute("exception.type", exception.getClass().getName());
                attribute("exception.message", exception.getMessage());
            }

            @Override
            public void close() {
                traces.export(this);
            }

            String toJson(String resourceId, String serviceName) {
                Duration duration = Duration.between(start, Instant.now());
                return """
                    {
                      "traceId": "%s",
                      "spanId": "%s",
                      "parentSpanId": %s,
                      "name": "%s",
                      "resourceId": "%s",
                      "serviceName": "%s",
                      "kind": "%s",
                      "status": "%s",
                      "startTime": "%s",
                      "duration": "%s",
                      "attributes": %s
                    }
                    """.formatted(
                        escapeJson(context.traceId()),
                        escapeJson(context.spanId()),
                        context.parentSpanId() == null
                            ? "null"
                            : "\"" + escapeJson(context.parentSpanId()) + "\"",
                        escapeJson(name),
                        escapeJson(resourceId),
                        escapeJson(serviceName),
                        escapeJson(kind),
                        escapeJson(status),
                        start.toString(),
                        formatDuration(duration),
                        attributesToJson(attributes));
            }

            private static String attributesToJson(Map<String, String> values) {
                if (values.isEmpty()) {
                    return "{}";
                }

                List<String> items = new ArrayList<>();
                for (Map.Entry<String, String> entry : values.entrySet()) {
                    items.add("\"" + escapeJson(entry.getKey()) + "\":\"" +
                        escapeJson(entry.getValue()) + "\"");
                }

                return "{" + String.join(",", items) + "}";
            }

            private static String formatDuration(Duration duration) {
                long ticks = Math.max(0, duration.toNanos() / 100);
                long days = ticks / 864_000_000_000L;
                ticks %= 864_000_000_000L;
                long hours = ticks / 36_000_000_000L;
                ticks %= 36_000_000_000L;
                long minutes = ticks / 600_000_000L;
                ticks %= 600_000_000L;
                long seconds = ticks / 10_000_000L;
                long fraction = ticks % 10_000_000L;
                String time = String.format(
                    "%02d:%02d:%02d.%07d",
                    hours,
                    minutes,
                    seconds,
                    fraction);
                return days > 0 ? days + "." + time : time;
            }
        }
    }

    private static String escapeJson(String value) {
        return value
            .replace("\\", "\\\\")
            .replace("\"", "\\\"");
    }

    private record RabbitMqOptions(
        String host,
        int port,
        String username,
        String password,
        String virtualHost,
        String exchange,
        String queue) {
        private static final String CONFIGURE_PERMISSION =
            "CloudShell.Messaging/rabbitMQ/configure/action";

        static RabbitMqOptions fromEnvironment() throws Exception {
            boolean requiresCloudShellCredentials =
                "CloudShell".equalsIgnoreCase(env("RABBITMQ_AUTHENTICATION", ""));
            String username = System.getenv("RABBITMQ_USERNAME");
            String password = System.getenv("RABBITMQ_PASSWORD");
            String virtualHost = env("RABBITMQ_VHOST", "/");
            if (requiresCloudShellCredentials) {
                RabbitMqCredential credentials = resolveCloudShellCredentials();
                username = credentials.username();
                password = credentials.password();
                virtualHost = credentials.virtualHost();
            }

            return new RabbitMqOptions(
                env("RABBITMQ_HOST", "localhost"),
                parsePort(env("RABBITMQ_PORT", "5672"), 5672),
                username == null || username.isBlank() ? "guest" : username,
                password == null || password.isBlank() ? "guest" : password,
                virtualHost,
                env("RABBITMQ_EXCHANGE", "cloudshell.sample.events"),
                env("RABBITMQ_QUEUE", "rabbitmq-java-events"));
        }

        private static RabbitMqCredential resolveCloudShellCredentials() throws Exception {
            String endpoint = env("RABBITMQ_CREDENTIAL_ENDPOINT", "");
            String resourceName = env("RABBITMQ_RESOURCE_NAME", "rabbitmq");
            String permission = env("RABBITMQ_CREDENTIAL_PERMISSION", CONFIGURE_PERMISSION);
            if (endpoint.isBlank()) {
                throw new IllegalStateException(
                    "RabbitMQ CloudShell authentication requires RABBITMQ_CREDENTIAL_ENDPOINT.");
            }

            HttpClient client = HttpClient.newBuilder()
                .connectTimeout(Duration.ofSeconds(5))
                .build();
            Exception lastException = null;
            long deadline = System.currentTimeMillis() + 60_000;
            while (System.currentTimeMillis() < deadline) {
                try {
                    String token = requestCloudShellToken(client, permission);
                    String body = "{\"rabbitMQResourceName\":\"" + escapeJson(resourceName) +
                        "\",\"permission\":\"" + escapeJson(permission) + "\"}";
                    HttpRequest request = HttpRequest.newBuilder(URI.create(endpoint))
                        .timeout(Duration.ofSeconds(10))
                        .header("Authorization", "Bearer " + token)
                        .header("Content-Type", "application/json")
                        .POST(HttpRequest.BodyPublishers.ofString(body))
                        .build();
                    HttpResponse<String> response = client.send(
                        request,
                        HttpResponse.BodyHandlers.ofString());
                    if (response.statusCode() == 401 || response.statusCode() == 403) {
                        throw new IllegalStateException(
                            "CloudShell denied the RabbitMQ credential request: " + response.body());
                    }

                    if (response.statusCode() < 200 || response.statusCode() > 299) {
                        lastException = new IllegalStateException(
                            "CloudShell RabbitMQ credential endpoint returned " +
                                response.statusCode() + ": " + response.body());
                        Thread.sleep(2_000);
                        continue;
                    }

                    return new RabbitMqCredential(
                        requireJsonString(response.body(), "username"),
                        requireJsonString(response.body(), "password"),
                        requireJsonString(response.body(), "virtualHost"));
                } catch (IOException | InterruptedException exception) {
                    lastException = exception;
                    if (exception instanceof InterruptedException) {
                        Thread.currentThread().interrupt();
                        throw exception;
                    }

                    Thread.sleep(2_000);
                }
            }

            throw new IllegalStateException(
                "CloudShell RabbitMQ credentials could not be resolved.",
                lastException);
        }

        private static String requestCloudShellToken(
                HttpClient client,
                String permission) throws IOException, InterruptedException {
            String tokenEndpoint = env("CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT", "");
            String clientId = env("CLOUDSHELL_IDENTITY_CLIENT_ID", "");
            String clientSecret = env("CLOUDSHELL_IDENTITY_CLIENT_SECRET", "");
            if (tokenEndpoint.isBlank() || clientId.isBlank() || clientSecret.isBlank()) {
                throw new IllegalStateException(
                    "CloudShell resource identity environment variables are not configured.");
            }

            String form =
                "grant_type=client_credentials" +
                "&client_id=" + urlEncode(clientId) +
                "&client_secret=" + urlEncode(clientSecret) +
                "&scope=" + urlEncode(permission);
            HttpRequest request = HttpRequest.newBuilder(URI.create(tokenEndpoint))
                .timeout(Duration.ofSeconds(10))
                .header("Content-Type", "application/x-www-form-urlencoded")
                .POST(HttpRequest.BodyPublishers.ofString(form))
                .build();
            HttpResponse<String> response = client.send(
                request,
                HttpResponse.BodyHandlers.ofString());
            if (response.statusCode() < 200 || response.statusCode() > 299) {
                throw new IllegalStateException(
                    "CloudShell identity token endpoint returned " +
                        response.statusCode() + ": " + response.body());
            }

            return requireJsonString(response.body(), "access_token");
        }

        private static String requireJsonString(String json, String name) {
            String value = readJsonString(json, name);
            if (value == null || value.isBlank()) {
                throw new IllegalStateException(
                    "Expected JSON string property '" + name + "'.");
            }

            return value;
        }

        private static String readJsonString(String json, String name) {
            String pattern = "\"" + name + "\"";
            int property = json.indexOf(pattern);
            if (property < 0) {
                return null;
            }

            int colon = json.indexOf(':', property + pattern.length());
            int start = colon < 0 ? -1 : json.indexOf('"', colon + 1);
            if (start < 0) {
                return null;
            }

            StringBuilder value = new StringBuilder();
            boolean escaped = false;
            for (int index = start + 1; index < json.length(); index++) {
                char current = json.charAt(index);
                if (escaped) {
                    value.append(current);
                    escaped = false;
                    continue;
                }

                if (current == '\\') {
                    escaped = true;
                    continue;
                }

                if (current == '"') {
                    return value.toString();
                }

                value.append(current);
            }

            return null;
        }

        private static String urlEncode(String value) {
            return URLEncoder.encode(value, StandardCharsets.UTF_8);
        }

        private static String env(String name, String fallback) {
            String value = System.getenv(name);
            return value == null || value.isBlank() ? fallback : value;
        }
    }

    private record RabbitMqCredential(
        String username,
        String password,
        String virtualHost) {
    }

    private static final class RabbitMqBroker implements AutoCloseable {
        private final RabbitMqOptions options;
        private final TraceSupport traces;
        private final Connection connection;
        private final Channel publishChannel;
        private final Channel consumeChannel;

        private RabbitMqBroker(
                RabbitMqOptions options,
                TraceSupport traces,
                Connection connection) throws IOException {
            this.options = options;
            this.traces = traces;
            this.connection = connection;
            this.publishChannel = connection.createChannel();
            this.consumeChannel = connection.createChannel();
        }

        static RabbitMqBroker connect(
                RabbitMqOptions options,
                MessageStore messages,
                TraceSupport traces) throws Exception {
            ConnectionFactory factory = new ConnectionFactory();
            factory.setHost(options.host());
            factory.setPort(options.port());
            factory.setUsername(options.username());
            factory.setPassword(options.password());
            factory.setVirtualHost(options.virtualHost());

            Exception lastException = null;
            long deadline = System.currentTimeMillis() + 60_000;
            while (System.currentTimeMillis() < deadline) {
                try {
                    Connection connection = factory.newConnection("cloudshell-rabbitmq-java-sample");
                    RabbitMqBroker broker = new RabbitMqBroker(options, traces, connection);
                    broker.configureTopology();
                    broker.startConsumer(messages);
                    return broker;
                } catch (Exception exception) {
                    lastException = exception;
                    Thread.sleep(2_000);
                }
            }

            throw new IllegalStateException(
                "RabbitMQ was not reachable at " + options.host() + ":" + options.port(),
                lastException);
        }

        synchronized MessageEnvelope publish(
                String message,
                String subject,
                TraceSupport.TraceContext parentContext) throws IOException {
            MessageEnvelope envelope = new MessageEnvelope(
                UUID.randomUUID().toString().replace("-", ""),
                "java",
                subject == null || subject.isBlank() ? "sample.event" : subject,
                message,
                Instant.now().toString());
            try (TraceSupport.Span span = traces.startSpan(
                    "rabbitmq publish",
                    "Producer",
                    parentContext)) {
                span.attribute("messaging.system", "rabbitmq");
                span.attribute("messaging.operation", "publish");
                span.attribute("messaging.destination.name", options.exchange());
                span.attribute("messaging.message.id", envelope.id());
                span.attribute("messaging.message.conversation_id", envelope.subject());

                Map<String, Object> headers = new HashMap<>();
                traces.inject(span.context(), headers);
                publishChannel.basicPublish(
                    options.exchange(),
                    "",
                    new com.rabbitmq.client.AMQP.BasicProperties.Builder()
                        .headers(headers)
                        .build(),
                    envelope.toJson().getBytes(StandardCharsets.UTF_8));
            }

            return envelope;
        }

        private void configureTopology() throws IOException {
            publishChannel.exchangeDeclare(options.exchange(), BuiltinExchangeType.FANOUT, true);
            consumeChannel.exchangeDeclare(options.exchange(), BuiltinExchangeType.FANOUT, true);
            consumeChannel.queueDeclare(options.queue(), false, false, false, null);
            consumeChannel.queueBind(options.queue(), options.exchange(), "");
        }

        private void startConsumer(MessageStore messages) throws IOException {
            DeliverCallback deliver = (consumerTag, delivery) -> {
                Map<String, Object> headers = delivery.getProperties() == null
                    ? Collections.emptyMap()
                    : delivery.getProperties().getHeaders();
                try (TraceSupport.Span span = traces.startSpan(
                        "rabbitmq consume",
                        "Consumer",
                        traces.extract(headers))) {
                    String body = new String(delivery.getBody(), StandardCharsets.UTF_8);
                    MessageEnvelope envelope = MessageEnvelope.fromJson(body);
                    span.attribute("messaging.system", "rabbitmq");
                    span.attribute("messaging.operation", "consume");
                    span.attribute("messaging.destination.name", options.queue());
                    span.attribute("messaging.message.id", envelope.id());
                    span.attribute("messaging.message.conversation_id", envelope.subject());
                    span.attribute("messaging.message.origin", envelope.origin());
                    messages.add(envelope);
                }
            };
            consumeChannel.basicConsume(options.queue(), true, deliver, consumerTag -> { });
        }

        @Override
        public void close() throws Exception {
            consumeChannel.close();
            publishChannel.close();
            connection.close();
        }
    }

    private static final class MessageStore {
        private static final int MAX_MESSAGES = 100;
        private final CopyOnWriteArrayList<MessageEnvelope> messages = new CopyOnWriteArrayList<>();

        void add(MessageEnvelope envelope) {
            messages.add(envelope);
            while (messages.size() > MAX_MESSAGES) {
                messages.remove(0);
            }
        }

        String toJson() {
            List<String> items = new ArrayList<>();
            for (MessageEnvelope message : messages) {
                items.add(message.toJson());
            }

            return "[" + String.join(",", items) + "]";
        }
    }

    private record MessageEnvelope(
        String id,
        String origin,
        String subject,
        String message,
        String timestamp) {

        String toJson() {
            return """
                {
                  "id": "%s",
                  "origin": "%s",
                  "subject": "%s",
                  "message": "%s",
                  "timestamp": "%s"
                }
                """.formatted(
                    escapeJson(id),
                    escapeJson(origin),
                    escapeJson(subject),
                    escapeJson(message),
                    escapeJson(timestamp));
        }

        static MessageEnvelope fromJson(String json) {
            return new MessageEnvelope(
                extractJsonString(json, "id").orElse(""),
                extractJsonString(json, "origin").orElse("unknown"),
                extractJsonString(json, "subject").orElse("sample.event"),
                extractJsonString(json, "message").orElse(json),
                extractJsonString(json, "timestamp").orElse(Instant.now().toString()));
        }
    }
}
