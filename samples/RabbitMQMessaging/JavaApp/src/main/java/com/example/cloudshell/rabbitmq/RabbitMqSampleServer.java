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
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.CopyOnWriteArrayList;

public final class RabbitMqSampleServer {
    private RabbitMqSampleServer() {
    }

    public static void main(String[] args) throws Exception {
        RabbitMqOptions options = RabbitMqOptions.fromEnvironment();
        MessageStore messages = new MessageStore();
        RabbitMqBroker broker = RabbitMqBroker.connect(options, messages);
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
            String message = readMessage(exchange);
            MessageEnvelope envelope = broker.publish(
                message == null || message.isBlank()
                    ? "Hello from the Java RabbitMQ sample."
                    : message,
                readQuery(exchange).getOrDefault("subject", "sample.event"));
            writeJson(exchange, 202, envelope.toJson());
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

        static RabbitMqOptions fromEnvironment() {
            return new RabbitMqOptions(
                env("RABBITMQ_HOST", "localhost"),
                parsePort(env("RABBITMQ_PORT", "5672"), 5672),
                env("RABBITMQ_USERNAME", "guest"),
                env("RABBITMQ_PASSWORD", "guest"),
                env("RABBITMQ_VHOST", "/"),
                env("RABBITMQ_EXCHANGE", "cloudshell.sample.events"),
                env("RABBITMQ_QUEUE", "rabbitmq-java-events"));
        }

        private static String env(String name, String fallback) {
            String value = System.getenv(name);
            return value == null || value.isBlank() ? fallback : value;
        }
    }

    private static final class RabbitMqBroker implements AutoCloseable {
        private final RabbitMqOptions options;
        private final Connection connection;
        private final Channel publishChannel;
        private final Channel consumeChannel;

        private RabbitMqBroker(
                RabbitMqOptions options,
                Connection connection) throws IOException {
            this.options = options;
            this.connection = connection;
            this.publishChannel = connection.createChannel();
            this.consumeChannel = connection.createChannel();
        }

        static RabbitMqBroker connect(
                RabbitMqOptions options,
                MessageStore messages) throws Exception {
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
                    RabbitMqBroker broker = new RabbitMqBroker(options, connection);
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

        synchronized MessageEnvelope publish(String message, String subject) throws IOException {
            MessageEnvelope envelope = new MessageEnvelope(
                UUID.randomUUID().toString().replace("-", ""),
                "java",
                subject == null || subject.isBlank() ? "sample.event" : subject,
                message,
                Instant.now().toString());
            publishChannel.basicPublish(
                options.exchange(),
                "",
                null,
                envelope.toJson().getBytes(StandardCharsets.UTF_8));
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
                String body = new String(delivery.getBody(), StandardCharsets.UTF_8);
                messages.add(MessageEnvelope.fromJson(body));
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
