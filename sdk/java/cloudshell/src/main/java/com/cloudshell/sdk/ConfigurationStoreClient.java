package com.cloudshell.sdk;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Properties;

public final class ConfigurationStoreClient {
    public static final String DEFAULT_SCOPE = "ControlPlane.Access";

    private final URI settingsEndpoint;
    private final CloudShellTokenCredential credential;
    private final String[] scopes;
    private final HttpClient httpClient;

    public ConfigurationStoreClient(URI settingsEndpoint) {
        this(settingsEndpoint, new EnvironmentTokenCredential(), new String[] { DEFAULT_SCOPE }, HttpClient.newHttpClient());
    }

    public ConfigurationStoreClient(
        URI settingsEndpoint,
        CloudShellTokenCredential credential,
        String[] scopes,
        HttpClient httpClient) {
        this.settingsEndpoint = settingsEndpoint;
        this.credential = credential;
        this.scopes = scopes == null || scopes.length == 0 ? new String[] { DEFAULT_SCOPE } : scopes;
        this.httpClient = httpClient;
    }

    public static ConfigurationStoreClient fromEnvironment() {
        return tryFromEnvironment(null).orElseThrow(() ->
            new IllegalStateException("No CloudShell configuration store endpoint was found in the environment."));
    }

    public static Optional<ConfigurationStoreClient> tryFromEnvironment(String serviceName) {
        return CloudShellEnvironment
            .findEndpoint("CLOUDSHELL_CONFIGURATION_", serviceName, System.getenv())
            .map(ConfigurationStoreClient::new);
    }

    public List<CloudShellConfigurationSetting> getSettings()
            throws IOException, InterruptedException {
        String body = send(settingsEndpoint);
        return CloudShellJson.objects(body).stream()
            .map(object -> new CloudShellConfigurationSetting(
                CloudShellJson.stringProperty(object, "name"),
                CloudShellJson.stringProperty(object, "value")))
            .toList();
    }

    public Optional<CloudShellConfigurationSetting> getSetting(String name)
            throws IOException, InterruptedException {
        HttpResponse<String> response = sendRaw(buildSettingEndpoint(name));
        if (response.statusCode() == 404) {
            return Optional.empty();
        }

        ensureSuccess(response);
        String body = response.body();
        return Optional.of(new CloudShellConfigurationSetting(
            CloudShellJson.stringProperty(body, "name"),
            CloudShellJson.stringProperty(body, "value")));
    }

    public Map<String, String> toMap(boolean mapPortableHierarchySeparator)
            throws IOException, InterruptedException {
        Map<String, String> values = new LinkedHashMap<>();
        for (CloudShellConfigurationSetting setting : getSettings()) {
            values.put(
                mapPortableHierarchySeparator
                    ? setting.name().replace("--", ".")
                    : setting.name(),
                setting.value());
        }

        return values;
    }

    public Properties toProperties(boolean mapPortableHierarchySeparator)
            throws IOException, InterruptedException {
        Properties properties = new Properties();
        properties.putAll(toMap(mapPortableHierarchySeparator));
        return properties;
    }

    public URI buildSettingEndpoint(String name) {
        if (name == null || name.isBlank()) {
            throw new IllegalArgumentException("Configuration setting name is required.");
        }

        String path = settingsEndpoint.getPath().replaceAll("/+$", "");
        return settingsEndpoint.resolve(path + "/" + urlEncode(name));
    }

    private String send(URI uri) throws IOException, InterruptedException {
        HttpResponse<String> response = sendRaw(uri);
        ensureSuccess(response);
        return response.body();
    }

    private HttpResponse<String> sendRaw(URI uri) throws IOException, InterruptedException {
        String token = credential.getToken(scopes);
        HttpRequest.Builder requestBuilder = HttpRequest.newBuilder(uri).GET();
        if (token != null && !token.isBlank()) {
            requestBuilder.header("Authorization", "Bearer " + token.trim());
        }

        HttpRequest request = requestBuilder.build();
        return httpClient.send(request, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
    }

    private static void ensureSuccess(HttpResponse<String> response) {
        if (response.statusCode() >= 200 && response.statusCode() <= 299) {
            return;
        }

        throw new IllegalStateException(
            response.body() == null || response.body().isBlank()
                ? "CloudShell Configuration Store returned " + response.statusCode() + "."
                : "CloudShell Configuration Store returned " + response.statusCode() + ". " + response.body());
    }

    private static String urlEncode(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8).replace("+", "%20");
    }
}
