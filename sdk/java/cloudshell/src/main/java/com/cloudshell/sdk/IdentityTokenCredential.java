package com.cloudshell.sdk;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.util.Map;

public final class IdentityTokenCredential implements CloudShellTokenCredential {
    public static final String TOKEN_ENDPOINT_ENVIRONMENT_VARIABLE = "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT";
    public static final String CLIENT_ID_ENVIRONMENT_VARIABLE = "CLOUDSHELL_IDENTITY_CLIENT_ID";
    public static final String CLIENT_SECRET_ENVIRONMENT_VARIABLE = "CLOUDSHELL_IDENTITY_CLIENT_SECRET";
    public static final String SCOPE_ENVIRONMENT_VARIABLE = "CLOUDSHELL_IDENTITY_SCOPE";
    public static final String DEFAULT_SCOPE = "ControlPlane.Access";

    private final Map<String, String> environment;
    private final HttpClient httpClient;
    private final String tokenEndpoint;
    private final String clientId;
    private final String clientSecret;
    private final String scope;
    private String cachedToken;
    private long cachedExpiresAt;

    public IdentityTokenCredential() {
        this(System.getenv(), HttpClient.newHttpClient(), null, null, null, null);
    }

    public IdentityTokenCredential(
        Map<String, String> environment,
        HttpClient httpClient,
        String tokenEndpoint,
        String clientId,
        String clientSecret,
        String scope) {
        this.environment = environment == null ? Map.of() : environment;
        this.httpClient = httpClient == null ? HttpClient.newHttpClient() : httpClient;
        this.tokenEndpoint = tokenEndpoint;
        this.clientId = clientId;
        this.clientSecret = clientSecret;
        this.scope = scope;
    }

    @Override
    public synchronized String getToken(String[] scopes) {
        if (cachedToken != null && cachedExpiresAt > System.currentTimeMillis() + 60_000) {
            return cachedToken;
        }

        String resolvedTokenEndpoint = firstNotBlank(
            tokenEndpoint,
            environment.get(TOKEN_ENDPOINT_ENVIRONMENT_VARIABLE));
        String resolvedClientId = firstNotBlank(
            clientId,
            environment.get(CLIENT_ID_ENVIRONMENT_VARIABLE));
        String resolvedClientSecret = firstNotBlank(
            clientSecret,
            environment.get(CLIENT_SECRET_ENVIRONMENT_VARIABLE));
        if (resolvedTokenEndpoint == null ||
            resolvedClientId == null ||
            resolvedClientSecret == null) {
            return null;
        }

        HttpRequest request = HttpRequest.newBuilder(URI.create(resolvedTokenEndpoint))
            .header("Content-Type", "application/x-www-form-urlencoded")
            .POST(HttpRequest.BodyPublishers.ofString(formBody(
                resolvedClientId,
                resolvedClientSecret,
                resolveScope(scopes))))
            .build();

        HttpResponse<String> response;
        try {
            response = httpClient.send(
                request,
                HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
        } catch (InterruptedException exception) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("CloudShell identity token request was interrupted.", exception);
        } catch (IOException exception) {
            throw new IllegalStateException("CloudShell identity token request failed.", exception);
        }

        if (response.statusCode() < 200 || response.statusCode() > 299) {
            throw new IllegalStateException(
                response.body() == null || response.body().isBlank()
                    ? "CloudShell identity token endpoint returned " + response.statusCode() + "."
                    : "CloudShell identity token endpoint returned " + response.statusCode() + ". " + response.body());
        }

        String accessToken = CloudShellJson.stringProperty(response.body(), "access_token");
        if (accessToken == null || accessToken.isBlank()) {
            throw new IllegalStateException("CloudShell identity token endpoint returned no access token.");
        }

        cachedToken = accessToken.trim();
        cachedExpiresAt = System.currentTimeMillis() +
            Math.max(0, intProperty(response.body(), "expires_in")) * 1000L;
        return cachedToken;
    }

    private String resolveScope(String[] scopes) {
        if (scopes != null && scopes.length > 0) {
            return String.join(" ", scopes);
        }

        return firstNotBlank(
            scope,
            environment.get(SCOPE_ENVIRONMENT_VARIABLE),
            DEFAULT_SCOPE);
    }

    private static String formBody(
        String clientId,
        String clientSecret,
        String scope) {
        return "grant_type=client_credentials" +
            "&client_id=" + urlEncode(clientId) +
            "&client_secret=" + urlEncode(clientSecret) +
            "&scope=" + urlEncode(scope);
    }

    private static String urlEncode(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8).replace("+", "%20");
    }

    private static int intProperty(String json, String name) {
        String value = CloudShellJson.numberProperty(json, name);
        if (value == null || value.isBlank()) {
            return 0;
        }

        return Integer.parseInt(value);
    }

    private static String firstNotBlank(String... values) {
        for (String value : values) {
            if (value != null && !value.isBlank()) {
                return value.trim();
            }
        }

        return null;
    }
}
