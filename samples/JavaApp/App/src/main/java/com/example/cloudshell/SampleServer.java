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
        int port = parsePort(System.getenv().getOrDefault("PORT", "5185"));
        HttpServer server = HttpServer.create(new InetSocketAddress("localhost", port), 0);
        server.createContext("/", exchange -> writeJson(exchange, 200, """
            {
              "message": "Hello from the CloudShell Java app sample",
              "configuredMessage": "%s",
              "secretAvailable": %s,
              "resourceId": "%s",
              "resourceName": "%s",
              "timestamp": "%s"
            }
            """.formatted(
                escape(readConfiguredMessage()),
                readSecretAvailable(),
                escape(System.getenv().getOrDefault("CLOUDSHELL_RESOURCE_ID", "")),
                escape(System.getenv().getOrDefault("CLOUDSHELL_RESOURCE_NAME", "")),
                Instant.now())));
        server.createContext("/healthz", exchange -> writeText(exchange, 200, "healthy"));
        server.createContext("/alive", exchange -> writeText(exchange, 200, "alive"));
        server.start();
        System.out.printf("CloudShell Java sample listening on http://localhost:%d%n", port);
    }

    private static int parsePort(String value) {
        try {
            return Integer.parseInt(value);
        } catch (NumberFormatException exception) {
            return 5185;
        }
    }

    private static String readConfiguredMessage() {
        try {
            return ConfigurationStoreClient
                .tryFromEnvironment(System.getenv("CLOUDSHELL_CONFIGURATION_SERVICE_NAME"))
                .flatMap(client -> {
                    try {
                        return client.getEntry("Sample--Message");
                    } catch (Exception exception) {
                        return java.util.Optional.empty();
                    }
                })
                .map(entry -> entry.value())
                .orElse("No CloudShell configuration value was loaded.");
        } catch (Exception exception) {
            return "No CloudShell configuration value was loaded.";
        }
    }

    private static boolean readSecretAvailable() {
        try {
            return SecretsVaultClient
                .tryFromEnvironment(System.getenv("CLOUDSHELL_SECRETS_VAULT_NAME"))
                .flatMap(client -> {
                    try {
                        return client.getSecret("Sample--Secret");
                    } catch (Exception exception) {
                        return java.util.Optional.empty();
                    }
                })
                .isPresent();
        } catch (Exception exception) {
            return false;
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
        return value
            .replace("\\", "\\\\")
            .replace("\"", "\\\"");
    }
}
