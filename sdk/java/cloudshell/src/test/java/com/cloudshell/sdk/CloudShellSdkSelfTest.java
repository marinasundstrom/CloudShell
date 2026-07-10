package com.cloudshell.sdk;

import com.sun.net.httpserver.Headers;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.URI;
import java.net.http.HttpClient;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Properties;

public final class CloudShellSdkSelfTest {
    private CloudShellSdkSelfTest() {
    }

    public static void main(String[] args) throws Exception {
        readsConfigurationStoreWithBearerToken();
        readsSecretsVaultWithBearerToken();
        discoversNamedEndpoints();
        defaultCredentialUsesFirstAvailableToken();
        System.out.println("CloudShell Java SDK self-test passed.");
    }

    private static void readsConfigurationStoreWithBearerToken() throws Exception {
        try (TestServer server = TestServer.start()) {
            server.addJson("/settings", "[{\"name\":\"Sample--Message\",\"value\":\"Hello\"}]");
            server.addJson("/settings/Sample--Message", "{\"name\":\"Sample--Message\",\"value\":\"Hello\"}");
            server.addJson("/settings/Missing", null, 404);

            ConfigurationStoreClient client = new ConfigurationStoreClient(
                server.uri("/settings"),
                new StaticTokenCredential("configuration-token"),
                new String[] { "scope" },
                HttpClient.newHttpClient());

            List<CloudShellConfigurationSetting> settings = client.getSettings();
            assertEquals(1, settings.size(), "configuration setting count");
            assertEquals("Sample--Message", settings.getFirst().name(), "configuration setting name");
            assertEquals("Hello", settings.getFirst().value(), "configuration setting value");

            CloudShellConfigurationSetting setting = client
                .getSetting("Sample--Message")
                .orElseThrow(() -> new AssertionError("configuration setting was not returned"));
            assertEquals("Hello", setting.value(), "single configuration setting value");
            assertTrue(client.getSetting("Missing").isEmpty(), "missing configuration setting should be empty");

            Properties properties = client.toProperties(true);
            assertEquals("Hello", properties.getProperty("Sample.Message"), "mapped configuration property");
            assertEquals(
                "Bearer configuration-token",
                server.lastAuthorization(),
                "configuration authorization header");
        }
    }

    private static void readsSecretsVaultWithBearerToken() throws Exception {
        try (TestServer server = TestServer.start()) {
            server.addJson("/secrets", "[{\"name\":\"Sample--Secret\",\"version\":\"v1\"}]");
            server.addJson(
                "/secrets/Sample--Secret?version=v1",
                "{\"name\":\"Sample--Secret\",\"value\":\"secret-value\",\"version\":\"v1\"}");
            server.addJson("/secrets/Missing", null, 404);

            SecretsVaultClient client = new SecretsVaultClient(
                server.uri("/secrets"),
                new StaticTokenCredential("secrets-token"),
                new String[] { "scope" },
                HttpClient.newHttpClient());

            List<SecretProperties> secrets = client.getSecrets();
            assertEquals(1, secrets.size(), "secret count");
            assertEquals("Sample--Secret", secrets.getFirst().name(), "secret name");
            assertEquals("v1", secrets.getFirst().version(), "secret version");

            SecretValue secret = client
                .getSecret("Sample--Secret", "v1")
                .orElseThrow(() -> new AssertionError("secret was not returned"));
            assertEquals("secret-value", secret.value(), "secret value");
            assertTrue(client.getSecret("Missing").isEmpty(), "missing secret should be empty");
            assertEquals("Bearer secrets-token", server.lastAuthorization(), "secrets authorization header");
        }
    }

    private static void discoversNamedEndpoints() {
        Map<String, String> environment = Map.of(
            "CLOUDSHELL_CONFIGURATION_OTHER_ENDPOINT", "http://localhost/other",
            "CLOUDSHELL_CONFIGURATION_APP_SETTINGS_ENDPOINT", "http://localhost/settings",
            "CLOUDSHELL_SECRETS_APP_VAULT_ENDPOINT", "http://localhost/secrets",
            "CLOUDSHELL_SECRETS_IGNORED_ENDPOINT", "not-a-uri");

        URI settings = CloudShellEnvironment
            .findEndpoint("CLOUDSHELL_CONFIGURATION_", "app-settings", environment)
            .orElseThrow(() -> new AssertionError("configuration endpoint was not discovered"));
        URI secrets = CloudShellEnvironment
            .findEndpoint("CLOUDSHELL_SECRETS_", "app-vault", environment)
            .orElseThrow(() -> new AssertionError("secrets endpoint was not discovered"));

        assertEquals("http://localhost/settings", settings.toString(), "named configuration endpoint");
        assertEquals("http://localhost/secrets", secrets.toString(), "named secrets endpoint");
    }

    private static void defaultCredentialUsesFirstAvailableToken() {
        List<String> called = new ArrayList<>();
        DefaultCloudShellTokenCredential credential = new DefaultCloudShellTokenCredential(
            scopes -> {
                called.add("empty");
                return "";
            },
            scopes -> {
                called.add("token");
                return " selected-token ";
            },
            scopes -> {
                called.add("unused");
                return "unused-token";
            });

        assertEquals("selected-token", credential.getToken(new String[] { "scope" }), "default credential token");
        assertEquals(List.of("empty", "token"), called, "default credential call order");
    }

    private static void assertEquals(Object expected, Object actual, String label) {
        if (!expected.equals(actual)) {
            throw new AssertionError(label + ": expected '" + expected + "' but got '" + actual + "'.");
        }
    }

    private static void assertTrue(boolean condition, String label) {
        if (!condition) {
            throw new AssertionError(label);
        }
    }

    private static final class TestServer implements AutoCloseable {
        private final HttpServer server;
        private volatile String lastAuthorization;

        private TestServer(HttpServer server) {
            this.server = server;
        }

        static TestServer start() throws IOException {
            HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
            server.start();
            return new TestServer(server);
        }

        URI uri(String path) {
            return URI.create("http://127.0.0.1:" + server.getAddress().getPort() + path);
        }

        String lastAuthorization() {
            return lastAuthorization;
        }

        void addJson(String pathAndQuery, String body) {
            addJson(pathAndQuery, body, 200);
        }

        void addJson(String pathAndQuery, String body, int statusCode) {
            String[] parts = pathAndQuery.split("\\?", 2);
            String path = parts[0];
            String query = parts.length == 2 ? parts[1] : null;
            server.createContext(path, exchange -> {
                if (query != null &&
                    !query.equals(exchange.getRequestURI().getRawQuery())) {
                    send(exchange, 404, "");
                    return;
                }

                Headers headers = exchange.getRequestHeaders();
                lastAuthorization = headers.getFirst("Authorization");
                send(exchange, statusCode, body == null ? "" : body);
            });
        }

        private static void send(HttpExchange exchange, int statusCode, String body) throws IOException {
            byte[] payload = body.getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(statusCode, payload.length);
            exchange.getResponseBody().write(payload);
            exchange.close();
        }

        @Override
        public void close() {
            server.stop(0);
        }
    }
}
