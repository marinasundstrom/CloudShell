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
            .withEndpoint("http://localhost:5104")
            .withSeed(seed -> seed.setting("Sample--Message", "Hello from Java"));

        SecretsVaultResource secrets = app.addSecretsVault("secrets")
            .withDisplayName("Secrets")
            .withEndpoint("http://localhost:6104")
            .withSeed(seed -> seed
                .secret("Sample--ApiKey", "java-secret", "v1")
                .certificate("ApiTls", "java-certificate", "v1", "application/x-pem-file"));

        JavaAppResource api = app.addJavaMavenApp("api", "samples/JavaApp/App", "target/app.jar", "clean package -DskipTests")
            .withDisplayName("Java API")
            .withServiceDiscovery()
            .withEnvironmentVariable("PORT", "5186")
            .withEnvironmentVariable("Sample__Message", settings.setting("Sample--Message"))
            .withEnvironmentVariable("Sample__ApiKey", secrets.secret("Sample--ApiKey"))
            .withReference(settings)
            .withReference(secrets)
            .withHttpEndpoint("localhost", 5186, 5186, network)
            .withHttpHealthCheck("/ready")
            .withHttpLivenessCheck("/live")
            .withDefaultConsoleLogSource();

        app.addLoadBalancer("edge")
            .withDisplayName("Edge")
            .withProvider("traefik")
            .useHost(network)
            .exposeHttps(secrets.certificate("ApiTls"), 4443)
            .mapHost("api.local", api, 5186, "https");

        String json = app.toJson();
        assertContains(json, "\"type\": \"application.java-app\"");
        assertContains(json, "\"providerId\": \"applications.java-app\"");
        assertContains(json, "\"buildTool\": \"maven\"");
        assertContains(json, "\"buildArguments\": \"clean package -DskipTests\"");
        assertContains(json, "\"resourceId\": \"configuration.store:settings\"");
        assertContains(json, "\"providerId\": \"secrets-vault\"");
        assertContains(json, "\"seed\": {");
        assertContains(json, "\"entries\": [");
        assertContains(json, "\"name\": \"Sample--Message\"");
        assertContains(json, "\"value\": \"Hello from Java\"");
        assertContains(json, "\"secrets\": [");
        assertContains(json, "\"name\": \"Sample--ApiKey\"");
        assertContains(json, "\"value\": \"java-secret\"");
        assertContains(json, "\"version\": \"v1\"");
        assertContains(json, "\"certificates\": [");
        assertContains(json, "\"name\": \"ApiTls\"");
        assertContains(json, "\"value\": \"java-certificate\"");
        assertContains(json, "\"contentType\": \"application/x-pem-file\"");
        assertContains(json, "\"configurationEntryRef\": { \"storeResourceId\": \"configuration.store:settings\", \"name\": \"Sample--Message\" }");
        assertContains(json, "\"secretRef\": { \"vaultResourceId\": \"secrets.vault:secrets\", \"name\": \"Sample--ApiKey\" }");
        assertContains(json, "\"path\": \"/ready\"");
        assertContains(json, "\"path\": \"/live\"");
        assertContains(json, "\"capabilities\": [\"read\", \"stream\"]");
        assertContains(json, "\"type\": \"cloudshell.loadBalancer\"");
        assertContains(json, "\"providerId\": \"cloudshell.load-balancer\"");
        assertContains(json, "\"loadBalancer\": {");
        assertContains(json, "\"provider\": \"traefik\"");
        assertContains(json, "\"hostResourceId\": \"network:host\"");
        assertContains(json, "\"entrypointDefinitions\": [");
        assertContains(json, "\"certificateRef\": {");
        assertContains(json, "\"vaultResourceId\": \"secrets.vault:secrets\"");
        assertContains(json, "\"routeDefinitions\": [");
        assertContains(json, "\"entrypointName\": \"https\"");
        assertContains(json, "\"host\": \"api.local\"");
        assertContains(json, "\"resourceId\": \"application.java-app:api\"");
        assertContains(json, "\"relationship\": \"reference\"");

        app.addJavaApp("worker", "samples/JavaApp/App", "target/worker.jar")
            .dependsOn(settings)
            .dependsOn("secrets.vault:external");
        app.addJavaGradleApp("gradle-worker", "samples/JavaApp/App", "build/libs/worker.jar");

        String dependencyJson = app.toJson();
        assertContains(dependencyJson, "\"dependsOn\": [");
        assertContains(dependencyJson, "\"relationship\": \"dependsOn\"");
        assertContains(dependencyJson, "\"resourceId\": \"secrets.vault:external\"");
        assertContains(dependencyJson, "\"buildTool\": \"gradle\"");
        assertContains(dependencyJson, "\"buildArguments\": \"build\"");

        CloudShellApp containerApp = CloudShellApp.create("java-container-test");
        NetworkResource containerNetwork = containerApp.addNetwork("host")
            .withResourceId("network:host")
            .withNetworkKind("Host");
        containerApp.addJavaMavenApp("api", "samples/JavaApp/App", "target/app.jar", "clean package")
            .withHttpEndpoint("localhost", 5185, 8080, containerNetwork)
            .asContainerApp("dev", "Dockerfile");
        String containerJson = containerApp.toJson();
        assertContains(containerJson, "\"type\": \"application.container-app\"");
        assertContains(containerJson, "\"providerId\": \"applications.container-app\"");
        assertContains(containerJson, "\"resourceId\": \"application.container-app:api\"");
        assertContains(containerJson, "\"buildTool\": \"maven\"");
        assertContains(containerJson, "\"buildArguments\": \"clean package\"");
        assertContains(containerJson, "\"container\": {");
        assertContains(containerJson, "\"image\": \"cloudshell-java-api:dev\"");
        assertContains(containerJson, "\"buildContext\": \"samples/JavaApp/App\"");
        assertContains(containerJson, "\"dockerfile\": \"Dockerfile\"");
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
