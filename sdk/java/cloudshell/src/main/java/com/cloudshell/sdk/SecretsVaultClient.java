package com.cloudshell.sdk;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Optional;

public final class SecretsVaultClient {
    public static final String DEFAULT_SCOPE = "ControlPlane.Access";

    private final URI secretsEndpoint;
    private final CloudShellTokenCredential credential;
    private final String[] scopes;
    private final HttpClient httpClient;

    public SecretsVaultClient(URI secretsEndpoint) {
        this(secretsEndpoint, new EnvironmentTokenCredential(), new String[] { DEFAULT_SCOPE }, HttpClient.newHttpClient());
    }

    public SecretsVaultClient(
        URI secretsEndpoint,
        CloudShellTokenCredential credential,
        String[] scopes,
        HttpClient httpClient) {
        this.secretsEndpoint = secretsEndpoint;
        this.credential = credential;
        this.scopes = scopes == null || scopes.length == 0 ? new String[] { DEFAULT_SCOPE } : scopes;
        this.httpClient = httpClient;
    }

    public static SecretsVaultClient fromEnvironment() {
        return tryFromEnvironment(null).orElseThrow(() ->
            new IllegalStateException("No CloudShell Secrets Vault endpoint was found in the environment."));
    }

    public static Optional<SecretsVaultClient> tryFromEnvironment(String vaultName) {
        return CloudShellEnvironment
            .findEndpoint("CLOUDSHELL_SECRETS_", vaultName, System.getenv())
            .map(SecretsVaultClient::new);
    }

    public List<SecretProperties> getSecrets()
            throws IOException, InterruptedException {
        String body = send(secretsEndpoint);
        return CloudShellJson.objects(body).stream()
            .map(object -> new SecretProperties(
                CloudShellJson.stringProperty(object, "name"),
                CloudShellJson.stringProperty(object, "version")))
            .toList();
    }

    public Optional<SecretValue> getSecret(String name)
            throws IOException, InterruptedException {
        return getSecret(name, null);
    }

    public Optional<SecretValue> getSecret(String name, String version)
            throws IOException, InterruptedException {
        HttpResponse<String> response = sendRaw(buildSecretEndpoint(name, version));
        if (response.statusCode() == 404) {
            return Optional.empty();
        }

        ensureSuccess(response);
        String body = response.body();
        return Optional.of(new SecretValue(
            CloudShellJson.stringProperty(body, "name"),
            CloudShellJson.stringProperty(body, "value"),
            CloudShellJson.stringProperty(body, "version")));
    }

    public URI buildSecretEndpoint(String name, String version) {
        if (name == null || name.isBlank()) {
            throw new IllegalArgumentException("Secret name is required.");
        }

        String path = secretsEndpoint.getPath().replaceAll("/+$", "");
        URI endpoint = secretsEndpoint.resolve(path + "/" + urlEncode(name));
        return version == null || version.isBlank()
            ? endpoint
            : URI.create(endpoint + "?version=" + urlEncode(version));
    }

    private String send(URI uri) throws IOException, InterruptedException {
        HttpResponse<String> response = sendRaw(uri);
        ensureSuccess(response);
        return response.body();
    }

    private HttpResponse<String> sendRaw(URI uri) throws IOException, InterruptedException {
        String token = credential.getToken(scopes);
        if (token == null || token.isBlank()) {
            throw new IllegalStateException("CloudShell secrets credential returned no access token.");
        }

        HttpRequest request = HttpRequest.newBuilder(uri)
            .GET()
            .header("Authorization", "Bearer " + token.trim())
            .build();
        return httpClient.send(request, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
    }

    private static void ensureSuccess(HttpResponse<String> response) {
        if (response.statusCode() >= 200 && response.statusCode() <= 299) {
            return;
        }

        throw new IllegalStateException(
            response.body() == null || response.body().isBlank()
                ? "CloudShell Secrets Vault returned " + response.statusCode() + "."
                : "CloudShell Secrets Vault returned " + response.statusCode() + ". " + response.body());
    }

    private static String urlEncode(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8).replace("+", "%20");
    }
}
