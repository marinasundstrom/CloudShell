package com.cloudshell.launcher;

import java.nio.file.Path;
import java.util.List;

public final class CloudShellAppTest {
    private CloudShellAppTest() {
    }

    public static void main(String[] args) {
        buildsTemplateJson();
        buildsApplyAndStartCommands();
        buildsForegroundRunCommand();
        formatsHostUrlMessage();
    }

    private static void buildsTemplateJson() {
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
        assertContains(json, "\"providerId\": \"secrets-vault\"");
        assertContains(json, "\"attributes\": {");
        assertContains(json, "\"configuration\": {");
        assertContains(json, "\"secrets\": {");
        assertContains(json, "\"path\": \"/ready\"");
        assertContains(json, "\"path\": \"/live\"");
        assertContains(json, "\"capabilities\": [\"read\", \"stream\"]");

        app.addJavaApp("worker", "samples/JavaApp/App", "target/worker.jar")
            .dependsOn(settings)
            .dependsOn("secrets.vault:external");

        String dependencyJson = app.toJson();
        assertContains(dependencyJson, "\"dependsOn\": [");
        assertContains(dependencyJson, "\"relationship\": \"dependsOn\"");
        assertContains(dependencyJson, "\"resourceId\": \"secrets.vault:external\"");
    }

    private static void buildsApplyAndStartCommands() {
        CloudShellLauncherOptions options = new CloudShellLauncherOptions()
            .withCliProject(Path.of("CloudShell.Cli/CloudShell.Cli.csproj"))
            .withControlPlaneUrl("http://127.0.0.1:5100")
            .withStateDirectory(Path.of(".cloudshell"))
            .withHostProject(Path.of("CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj"))
            .withDataDirectory(Path.of(".cloudshell"))
            .withHostUrl("http://127.0.0.1:5100")
            .withNoBuild(true);

        CloudShellLauncher.Command apply = CloudShellLauncher.buildTemplateApplyCommand(
            Path.of(".cloudshell/resources.json"),
            options,
            false);
        assertEquals("dotnet", apply.name());
        assertEquals(List.of(
            "run",
            "--project",
            "CloudShell.Cli/CloudShell.Cli.csproj",
            "--",
            "template",
            "apply",
            ".cloudshell/resources.json",
            "--control-plane",
            "http://127.0.0.1:5100",
            "--state-dir",
            ".cloudshell",
            "--host-project",
            "CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj",
            "--data-dir",
            ".cloudshell",
            "--url",
            "http://127.0.0.1:5100",
            "--timeout-seconds",
            "60",
            "--mode",
            "create-or-update",
            "--no-build"), apply.arguments());

        CloudShellLauncher.Command start = CloudShellLauncher.buildTemplateApplyCommand(
            Path.of(".cloudshell/resources.json"),
            options,
            true);
        assertContains(start.arguments().toString(), "--start");
    }

    private static void buildsForegroundRunCommand() {
        CloudShellLauncherOptions options = new CloudShellLauncherOptions()
            .withHostProject(Path.of("CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj"))
            .withDataDirectory(Path.of(".cloudshell"))
            .withNoBuild(true);

        CloudShellLauncher.Command command = CloudShellLauncher.buildHostRunCommand(
            options,
            "http://127.0.0.1:5100");

        assertEquals("dotnet", command.name());
        assertEquals(List.of(
            "run",
            "--project",
            "CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj",
            "--no-build",
            "--",
            "--urls",
            "http://127.0.0.1:5100",
            "--CloudShell:DataDirectory",
            ".cloudshell"), command.arguments());
    }

    private static void formatsHostUrlMessage() {
        assertEquals(
            "CloudShell UI: http://127.0.0.1:5100",
            CloudShellLauncher.formatHostUrlMessage("http://127.0.0.1:5100/"));
    }

    private static void assertContains(String value, String expected) {
        if (!value.contains(expected)) {
            throw new AssertionError("Expected generated template to contain: " + expected + "\n" + value);
        }
    }

    private static void assertEquals(Object expected, Object actual) {
        if (!expected.equals(actual)) {
            throw new AssertionError("Expected " + expected + " but got " + actual);
        }
    }
}
