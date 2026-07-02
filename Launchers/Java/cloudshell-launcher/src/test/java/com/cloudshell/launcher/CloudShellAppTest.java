package com.cloudshell.launcher;

public final class CloudShellAppTest {
    private CloudShellAppTest() {
    }

    public static void main(String[] args) {
        CloudShellApp app = CloudShellApp.create("java-test")
            .withEnvironmentId("local")
            .withMetadata("cloudshell.source", "java");

        NetworkResource network = app.addNetwork("host")
            .withResourceId("network:host")
            .withDisplayName("Host network")
            .withNetworkKind("Host")
            .withHostReadiness("hostReady");

        ConfigurationStoreResource settings = app.addConfigurationStore("settings")
            .withDisplayName("Settings")
            .withEndpoint("http://localhost:5104");

        SecretsVaultResource secrets = app.addSecretsVault("secrets")
            .withDisplayName("Secrets")
            .withEndpoint("http://localhost:6104");

        app.addJavaApp("api", "samples/JavaApp/App", "target/app.jar")
            .withDisplayName("Java API")
            .withServiceDiscovery()
            .withEnvironmentVariable("PORT", "5186")
            .withReference(settings)
            .withReference(secrets)
            .withHttpEndpoint("localhost", 5186, 5186, network)
            .withHttpHealthCheck("/ready")
            .withHttpLivenessCheck("/live")
            .withDefaultConsoleLogSource();

        String json = app.toJson();
        assertContains(json, "\"type\": \"application.java-app\"");
        assertContains(json, "\"providerId\": \"applications.java-app\"");
        assertContains(json, "\"resourceId\": \"configuration.store:settings\"");
        assertContains(json, "\"path\": \"/ready\"");
        assertContains(json, "\"path\": \"/live\"");
        assertContains(json, "\"capabilities\": [\"read\", \"stream\"]");
    }

    private static void assertContains(String value, String expected) {
        if (!value.contains(expected)) {
            throw new AssertionError("Expected generated template to contain: " + expected + "\n" + value);
        }
    }
}
