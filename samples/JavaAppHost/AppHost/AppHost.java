import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

public final class AppHost {
    private AppHost() {
    }

    public static void main(String[] args) throws Exception {
        Path repoRoot = findRepositoryRoot(Path.of("").toAbsolutePath());
        Path sampleRoot = repoRoot.resolve("samples").resolve("JavaAppHost");
        Path javaAppRoot = repoRoot.resolve("samples").resolve("JavaApp").resolve("App");
        Path cliProject = pathFromEnv(
            "CLOUDSHELL_CLI_PROJECT",
            repoRoot.resolve("CloudShell.Cli").resolve("CloudShell.Cli.csproj"));
        Path hostProject = pathFromEnv(
            "CLOUDSHELL_HOST_PROJECT",
            repoRoot.resolve("CloudShell.LocalDevelopmentHost").resolve("CloudShell.LocalDevelopmentHost.csproj"));
        Path stateDir = pathFromEnv(
            "CLOUDSHELL_STATE_DIR",
            sampleRoot.resolve(".cloudshell"));
        Path dataDir = Path.of(argumentValue(args, "--data-dir",
            System.getenv().getOrDefault("CLOUDSHELL_DATA_DIR", stateDir.toString())));
        String controlPlaneUrl = argumentValue(args, "--control-plane",
            System.getenv().getOrDefault("CLOUDSHELL_CONTROL_PLANE_URL", "http://127.0.0.1:5100"));

        String template = buildTemplate(javaAppRoot).toJson();

        if (hasArgument(args, "--apply") || hasArgument(args, "--start") || hasArgument(args, "--run")) {
            Path templatePath = argumentPath(args, "--template-path");
            if (templatePath == null) {
                Files.createDirectories(stateDir);
                templatePath = stateDir.resolve("resources.json");
            }

            Files.writeString(templatePath, template, StandardCharsets.UTF_8);
            List<String> command = new ArrayList<>();
            command.add("dotnet");
            command.add("run");
            command.add("--project");
            command.add(cliProject.toString());
            command.add("--");
            command.add("template");
            command.add("apply");
            command.add(templatePath.toString());
            command.add("--control-plane");
            command.add(controlPlaneUrl);
            command.add("--state-dir");
            command.add(stateDir.toString());
            command.add("--host-project");
            command.add(hostProject.toString());
            command.add("--data-dir");
            command.add(dataDir.toString());
            command.add("--url");
            command.add(controlPlaneUrl);
            command.add("--mode");
            command.add("create-or-update");
            if (hasArgument(args, "--start") || hasArgument(args, "--run")) {
                command.add("--start");
            }

            if (hasArgument(args, "--no-build")) {
                command.add("--no-build");
            }

            System.exit(run(command, repoRoot));
        }

        System.out.print(template);
    }

    private static CloudShellApp buildTemplate(Path javaAppRoot) {
        CloudShellApp app = CloudShellApp.create("java-app-host")
            .withEnvironmentId("local")
            .withMetadata("cloudshell.source", "java")
            .withMetadata("cloudshell.sample", "JavaAppHost");

        NetworkResource hostNetwork = app.addNetwork("host")
            .withResourceId("network:host")
            .withDisplayName("Host network")
            .withNetworkKind("Host")
            .withHostReadiness("hostReady");

        ConfigurationStoreResource settings = app.addConfigurationStore("java-launcher-settings")
            .withDisplayName("Java Launcher Settings")
            .withEndpoint("http://localhost:5104");

        SecretsVaultResource secrets = app.addSecretsVault("java-launcher-secrets")
            .withDisplayName("Java Launcher Secrets")
            .withEndpoint("http://localhost:6104");

        app.addJavaApp(
                "java-launcher-api",
                javaAppRoot.toString(),
                "target/cloudshell-java-app-sample.jar")
            .withDisplayName("Java Launcher API")
            .withServiceDiscovery()
            .withEnvironmentVariable("PORT", "5186")
            .withEnvironmentVariable("OTEL_SERVICE_NAME", "java-launcher-api")
            .withReference(settings)
            .withReference(secrets)
            .withHttpEndpoint("localhost", 5186, 5186, hostNetwork)
            .withHttpHealthCheck("/healthz")
            .withHttpLivenessCheck("/alive")
            .withDefaultConsoleLogSource();

        return app;
    }

    private static int run(List<String> command, Path workingDirectory)
            throws IOException, InterruptedException {
        Process process = new ProcessBuilder(command)
            .directory(workingDirectory.toFile())
            .inheritIO()
            .start();
        return process.waitFor();
    }

    private static Path pathFromEnv(String name, Path fallback) {
        String value = System.getenv(name);
        return value == null || value.isBlank() ? fallback : Path.of(value);
    }

    private static Path argumentPath(String[] args, String name) {
        String value = argumentValue(args, name, null);
        return value == null || value.isBlank() ? null : Path.of(value);
    }

    private static boolean hasArgument(String[] args, String name) {
        for (String arg : args) {
            if (arg.equalsIgnoreCase(name)) {
                return true;
            }
        }

        return false;
    }

    private static String argumentValue(String[] args, String name, String fallback) {
        for (int index = 0; index < args.length - 1; index++) {
            if (args[index].equalsIgnoreCase(name)) {
                return args[index + 1];
            }
        }

        return fallback;
    }

    private static Path findRepositoryRoot(Path start) {
        Path directory = start;
        while (directory != null) {
            if (Files.exists(directory.resolve("CloudShell.slnx"))) {
                return directory;
            }

            directory = directory.getParent();
        }

        throw new IllegalStateException("Could not find CloudShell.slnx from " + start);
    }

    private static String json(String value) {
        return "\"" + value
            .replace("\\", "\\\\")
            .replace("\"", "\\\"")
            .replace("\n", "\\n")
            .replace("\r", "\\r") + "\"";
    }

    private static final class CloudShellApp {
        private final String name;
        private String environmentId;
        private final List<String[]> metadata = new ArrayList<>();
        private final List<ResourceBuilder> resources = new ArrayList<>();

        private CloudShellApp(String name) {
            this.name = name;
        }

        static CloudShellApp create(String name) {
            return new CloudShellApp(name);
        }

        CloudShellApp withEnvironmentId(String environmentId) {
            this.environmentId = environmentId;
            return this;
        }

        CloudShellApp withMetadata(String name, String value) {
            metadata.add(new String[] { name, value });
            return this;
        }

        NetworkResource addNetwork(String name) {
            NetworkResource resource = new NetworkResource(name);
            resources.add(resource);
            return resource;
        }

        JavaAppResource addJavaApp(String name, String projectPath, String artifactPath) {
            JavaAppResource resource = new JavaAppResource(name, projectPath, artifactPath);
            resources.add(resource);
            return resource;
        }

        ConfigurationStoreResource addConfigurationStore(String name) {
            ConfigurationStoreResource resource = new ConfigurationStoreResource(name);
            resources.add(resource);
            return resource;
        }

        SecretsVaultResource addSecretsVault(String name) {
            SecretsVaultResource resource = new SecretsVaultResource(name);
            resources.add(resource);
            return resource;
        }

        String toJson() {
            StringBuilder builder = new StringBuilder();
            builder.append("{\n");
            property(builder, 1, "name", json(name), true);
            property(builder, 1, "environmentId", json(environmentId), true);
            object(builder, 1, "metadata", metadata, true);
            builder.append("  \"resources\": [\n");
            for (int index = 0; index < resources.size(); index++) {
                builder.append(resources.get(index).toJson(2));
                builder.append(index == resources.size() - 1 ? "\n" : ",\n");
            }

            builder.append("  ]\n");
            builder.append("}\n");
            return builder.toString();
        }
    }

    private abstract static class ResourceBuilder {
        final String name;
        final String type;
        final String providerId;
        String resourceId;
        String displayName;

        ResourceBuilder(String name, String type, String providerId) {
            this.name = name;
            this.type = type;
            this.providerId = providerId;
            this.resourceId = type + ":" + name;
        }

        ResourceBuilder withResourceId(String resourceId) {
            this.resourceId = resourceId;
            return this;
        }

        ResourceBuilder withDisplayName(String displayName) {
            this.displayName = displayName;
            return this;
        }

        abstract String toJson(int indent);

        void appendCommon(StringBuilder builder, int indent) {
            property(builder, indent, "name", json(name), true);
            property(builder, indent, "type", json(type), true);
            property(builder, indent, "resourceId", json(resourceId), true);
            property(builder, indent, "providerId", json(providerId), true);
            if (displayName != null) {
                property(builder, indent, "displayName", json(displayName), true);
            }
        }
    }

    private static final class NetworkResource extends ResourceBuilder {
        private String networkKind = "Host";
        private String hostReadiness = "hostReady";

        NetworkResource(String name) {
            super(name, "cloudshell.network", "cloudshell.network");
        }

        @Override
        NetworkResource withResourceId(String resourceId) {
            super.withResourceId(resourceId);
            return this;
        }

        @Override
        NetworkResource withDisplayName(String displayName) {
            super.withDisplayName(displayName);
            return this;
        }

        NetworkResource withNetworkKind(String networkKind) {
            this.networkKind = networkKind;
            return this;
        }

        NetworkResource withHostReadiness(String hostReadiness) {
            this.hostReadiness = hostReadiness;
            return this;
        }

        @Override
        String toJson(int indent) {
            StringBuilder builder = new StringBuilder();
            line(builder, indent, "{");
            appendCommon(builder, indent + 1);
            line(builder, indent + 1, "\"network\": {");
            property(builder, indent + 2, "kind", json(networkKind), true);
            property(builder, indent + 2, "hostReadiness", json(hostReadiness), false);
            line(builder, indent + 1, "}");
            line(builder, indent, "}");
            return builder.toString();
        }
    }

    private static final class JavaAppResource extends ResourceBuilder {
        private final String projectPath;
        private final String artifactPath;
        private final List<String[]> environment = new ArrayList<>();
        private final List<ResourceBuilder> references = new ArrayList<>();
        private Endpoint endpoint;
        private boolean serviceDiscovery;
        private boolean healthCheck;
        private boolean livenessCheck;
        private boolean consoleLogs;

        JavaAppResource(String name, String projectPath, String artifactPath) {
            super(name, "application.java-app", "applications.java-app");
            this.projectPath = projectPath;
            this.artifactPath = artifactPath;
        }

        @Override
        JavaAppResource withDisplayName(String displayName) {
            super.withDisplayName(displayName);
            return this;
        }

        JavaAppResource withServiceDiscovery() {
            serviceDiscovery = true;
            return this;
        }

        JavaAppResource withEnvironmentVariable(String name, String value) {
            environment.add(new String[] { name, value });
            return this;
        }

        JavaAppResource withReference(ResourceBuilder resource) {
            references.add(resource);
            return this;
        }

        JavaAppResource withHttpEndpoint(
            String host,
            int port,
            int targetPort,
            NetworkResource network) {
            endpoint = new Endpoint(host, port, targetPort, network);
            return this;
        }

        JavaAppResource withHttpHealthCheck(String path) {
            healthCheck = true;
            return this;
        }

        JavaAppResource withHttpLivenessCheck(String path) {
            livenessCheck = true;
            return this;
        }

        JavaAppResource withDefaultConsoleLogSource() {
            consoleLogs = true;
            return this;
        }

        @Override
        String toJson(int indent) {
            StringBuilder builder = new StringBuilder();
            line(builder, indent, "{");
            appendCommon(builder, indent + 1);
            line(builder, indent + 1, "\"java\": {");
            property(builder, indent + 2, "command", json("java"), true);
            property(builder, indent + 2, "artifactPath", json(artifactPath), false);
            line(builder, indent + 1, "},");
            line(builder, indent + 1, "\"project\": {");
            property(builder, indent + 2, "path", json(projectPath), true);
            if (serviceDiscovery) {
                property(builder, indent + 2, "serviceDiscoveryName", json(name), true);
            }

            objectOfValueObjects(builder, indent + 2, "environmentVariables", environment, true);
            appendReferences(builder, indent + 2, references, true);
            if (endpoint != null) {
                endpoint.appendJson(builder, indent + 2);
            }

            line(builder, indent + 1, "}" + (healthCheck || livenessCheck || consoleLogs ? "," : ""));
            if (healthCheck || livenessCheck) {
                appendHealth(builder, indent + 1, consoleLogs);
            }

            if (consoleLogs) {
                appendLogs(builder, indent + 1);
            }

            line(builder, indent, "}");
            return builder.toString();
        }

        private void appendHealth(StringBuilder builder, int indent, boolean trailingComma) {
            line(builder, indent, "\"health\": {");
            line(builder, indent + 1, "\"checks\": [");
            line(builder, indent + 2, """
                {
                  "name": "health",
                  "type": "health",
                  "source": {
                    "kind": "http",
                    "http": {
                      "path": "/healthz",
                      "endpointName": "http"
                    }
                  }
                },""");
            line(builder, indent + 2, """
                {
                  "name": "alive",
                  "type": "liveness",
                  "source": {
                    "kind": "http",
                    "http": {
                      "path": "/alive",
                      "endpointName": "http"
                    }
                  }
                }""");
            line(builder, indent + 1, "]");
            line(builder, indent, "}" + (trailingComma ? "," : ""));
        }

        private void appendLogs(StringBuilder builder, int indent) {
            line(builder, indent, """
                "logs": {
                  "sources": [
                    {
                      "id": "console",
                      "name": "Console logs",
                      "kind": "processOutput",
                      "format": "plainText",
                      "capabilities": ["read", "stream"],
                      "description": "Provider-captured process console output.",
                      "origin": "providerDefault",
                      "purpose": "default",
                      "availability": "resourceRunning"
                    }
                  ]
                }""");
        }
    }

    private static final class ConfigurationStoreResource extends ResourceBuilder {
        private String endpoint;

        ConfigurationStoreResource(String name) {
            super(name, "configuration.store", "configuration");
        }

        @Override
        ConfigurationStoreResource withDisplayName(String displayName) {
            super.withDisplayName(displayName);
            return this;
        }

        ConfigurationStoreResource withEndpoint(String endpoint) {
            this.endpoint = endpoint;
            return this;
        }

        @Override
        String toJson(int indent) {
            StringBuilder builder = new StringBuilder();
            line(builder, indent, "{");
            appendCommon(builder, indent + 1);
            line(builder, indent + 1, "\"configuration\": {");
            property(builder, indent + 2, "endpoint", json(endpoint), false);
            line(builder, indent + 1, "}");
            line(builder, indent, "}");
            return builder.toString();
        }
    }

    private static final class SecretsVaultResource extends ResourceBuilder {
        private String endpoint;

        SecretsVaultResource(String name) {
            super(name, "secrets.vault", "secrets");
        }

        @Override
        SecretsVaultResource withDisplayName(String displayName) {
            super.withDisplayName(displayName);
            return this;
        }

        SecretsVaultResource withEndpoint(String endpoint) {
            this.endpoint = endpoint;
            return this;
        }

        @Override
        String toJson(int indent) {
            StringBuilder builder = new StringBuilder();
            line(builder, indent, "{");
            appendCommon(builder, indent + 1);
            line(builder, indent + 1, "\"secrets\": {");
            property(builder, indent + 2, "endpoint", json(endpoint), false);
            line(builder, indent + 1, "}");
            line(builder, indent, "}");
            return builder.toString();
        }
    }

    private record Endpoint(String host, int port, int targetPort, NetworkResource network) {
        void appendJson(StringBuilder builder, int indent) {
            line(builder, indent, "\"endpointRequests\": [");
            line(builder, indent + 1, "{");
            property(builder, indent + 2, "name", json("http"), true);
            property(builder, indent + 2, "protocol", json("http"), true);
            property(builder, indent + 2, "targetPort", Integer.toString(targetPort), true);
            property(builder, indent + 2, "host", json(host), true);
            property(builder, indent + 2, "port", Integer.toString(port), true);
            property(builder, indent + 2, "exposure", json("Local"), true);
            line(builder, indent + 2, "\"network\": {");
            property(builder, indent + 3, "resourceId", json(network.resourceId), true);
            property(builder, indent + 3, "relationship", json("reference"), true);
            property(builder, indent + 3, "addressingMode", json("resourceId"), true);
            property(builder, indent + 3, "typeId", json(network.type), true);
            property(builder, indent + 3, "providerId", json(network.providerId), false);
            line(builder, indent + 2, "}");
            line(builder, indent + 1, "}");
            line(builder, indent, "]");
        }
    }

    private static void object(
        StringBuilder builder,
        int indent,
        String name,
        List<String[]> values,
        boolean trailingComma) {
        line(builder, indent, json(name) + ": {");
        for (int index = 0; index < values.size(); index++) {
            String[] pair = values.get(index);
            property(builder, indent + 1, pair[0], json(pair[1]), index < values.size() - 1);
        }

        line(builder, indent, "}" + (trailingComma ? "," : ""));
    }

    private static void objectOfValueObjects(
        StringBuilder builder,
        int indent,
        String name,
        List<String[]> values,
        boolean trailingComma) {
        line(builder, indent, json(name) + ": {");
        for (int index = 0; index < values.size(); index++) {
            String[] pair = values.get(index);
            line(builder, indent + 1, json(pair[0]) + ": {");
            property(builder, indent + 2, "value", json(pair[1]), false);
            line(builder, indent + 1, "}" + (index < values.size() - 1 ? "," : ""));
        }

        line(builder, indent, "}" + (trailingComma ? "," : ""));
    }

    private static void appendReferences(
        StringBuilder builder,
        int indent,
        List<ResourceBuilder> references,
        boolean trailingComma) {
        line(builder, indent, "\"references\": [");
        for (int index = 0; index < references.size(); index++) {
            ResourceBuilder reference = references.get(index);
            line(builder, indent + 1, "{");
            property(builder, indent + 2, "resourceId", json(reference.resourceId), true);
            property(builder, indent + 2, "relationship", json("reference"), true);
            property(builder, indent + 2, "addressingMode", json("resourceId"), true);
            property(builder, indent + 2, "typeId", json(reference.type), true);
            property(builder, indent + 2, "providerId", json(reference.providerId), false);
            line(builder, indent + 1, "}" + (index < references.size() - 1 ? "," : ""));
        }

        line(builder, indent, "]" + (trailingComma ? "," : ""));
    }

    private static void property(
        StringBuilder builder,
        int indent,
        String name,
        String value,
        boolean trailingComma) {
        line(builder, indent, json(name) + ": " + value + (trailingComma ? "," : ""));
    }

    private static void line(StringBuilder builder, int indent, String value) {
        String prefix = "  ".repeat(indent);
        String[] lines = value.split("\\R", -1);
        for (String line : lines) {
            if (!line.isEmpty()) {
                builder.append(prefix).append(line);
            }

            builder.append('\n');
        }
    }
}
