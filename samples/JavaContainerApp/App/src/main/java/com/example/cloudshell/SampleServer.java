package com.example.cloudshell;

import com.cloudshell.sdk.ConfigurationStoreClient;
import com.cloudshell.sdk.SecretsVaultClient;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.time.Instant;

public final class SampleServer {
    private SampleServer() {
    }

    public static void main(String[] args) throws Exception {
        int port = parsePort(System.getenv().getOrDefault("PORT", "8080"));
        HttpServer server = HttpServer.create(new InetSocketAddress(port), 0);
        server.createContext("/", exchange -> writeJson(exchange, 200, """
            {
              "message": "CloudShell Java container app sample",
              "configuration": "/configuration",
              "health": "/healthz",
              "timestamp": "%s"
            }
            """.formatted(Instant.now())));
        server.createContext("/configuration", SampleServer::writeConfiguration);
        server.createContext("/healthz", exchange -> writeText(exchange, 200, "healthy"));
        server.createContext("/alive", exchange -> writeText(exchange, 200, "alive"));
        server.start();
        System.out.printf("CloudShell Java container sample listening on http://0.0.0.0:%d%n", port);
    }

    private static void writeConfiguration(HttpExchange exchange) throws IOException {
        String message = "";
        String secretName = "";
        boolean hasApiKey = false;
        try {
            var setting = ConfigurationStoreClient
                .tryFromEnvironment(System.getenv("CLOUDSHELL_CONFIGURATION_SERVICE_NAME"))
                .flatMap(client -> {
                    try {
                        return client.getSetting("Sample--Message");
                    } catch (Exception exception) {
                        return java.util.Optional.empty();
                    }
                });
            if (setting.isPresent()) {
                message = setting.get().value();
            }

            var secret = SecretsVaultClient
                .tryFromEnvironment(System.getenv("CLOUDSHELL_SECRETS_VAULT_NAME"))
                .flatMap(client -> {
                    try {
                        return client.getSecret("Sample--ApiKey");
                    } catch (Exception exception) {
                        return java.util.Optional.empty();
                    }
                });
            if (secret.isPresent()) {
                secretName = secret.get().name();
                hasApiKey = secret.get().value() != null && !secret.get().value().isBlank();
            }
        } catch (Exception exception) {
            writeJson(exchange, 503, """
                {
                  "source": "cloudshell-sdk",
                  "sdkError": "%s"
                }
                """.formatted(escape(exception.getMessage())));
            return;
        }

        writeJson(exchange, 200, """
            {
              "source": "cloudshell-sdk",
              "message": "%s",
              "hasApiKey": %s,
              "secretName": "%s",
              "resourceId": "%s",
              "resourceName": "%s"
            }
            """.formatted(
                escape(message),
                hasApiKey,
                escape(secretName),
                escape(System.getenv().getOrDefault("CLOUDSHELL_RESOURCE_ID", "")),
                escape(System.getenv().getOrDefault("CLOUDSHELL_RESOURCE_NAME", ""))));
    }

    private static int parsePort(String value) {
        try {
            return Integer.parseInt(value);
        } catch (NumberFormatException exception) {
            return 8080;
        }
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

    private static String escape(String value) {
        if (value == null) {
            return "";
        }

        return value
            .replace("\\", "\\\\")
            .replace("\"", "\\\"");
    }
}
