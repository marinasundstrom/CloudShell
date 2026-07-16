package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.appendReferences;
import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

import java.util.ArrayList;
import java.util.List;

public final class JavaAppResource extends ResourceBuilder<JavaAppResource> {
    private final String projectPath;
    private final String artifactPath;
    private final List<EnvironmentVariable> environment = new ArrayList<>();
    private final List<ResourceBuilder<?>> references = new ArrayList<>();
    private final List<HealthCheck> healthChecks = new ArrayList<>();
    private EndpointRequest endpoint;
    private String serviceDiscoveryName;
    private String buildTool;
    private String buildArguments;
    private boolean containerApp;
    private String containerImage;
    private String containerRegistry;
    private String containerBuildContext;
    private String containerDockerfile;
    private int containerReplicas = 1;
    private boolean consoleLogs;

    JavaAppResource(String name, String projectPath, String artifactPath) {
        super(name, "application.java-app", "applications.java-app");
        this.projectPath = projectPath;
        this.artifactPath = artifactPath;
    }

    public JavaAppResource withServiceDiscovery() {
        serviceDiscoveryName = name();
        return this;
    }

    public JavaAppResource withServiceDiscoveryName(String serviceDiscoveryName) {
        this.serviceDiscoveryName = serviceDiscoveryName;
        return this;
    }

    public JavaAppResource withMavenBuild() {
        return withMavenBuild("package");
    }

    public JavaAppResource withMavenBuild(String arguments) {
        buildTool = "maven";
        buildArguments = arguments;
        return this;
    }

    public JavaAppResource withGradleBuild() {
        return withGradleBuild("build");
    }

    public JavaAppResource withGradleBuild(String arguments) {
        buildTool = "gradle";
        buildArguments = arguments;
        return this;
    }

    public JavaAppResource asContainerApp() {
        return asContainerApp(null, null, null, null, null, 1);
    }

    public JavaAppResource asContainerApp(String tag, String dockerfile) {
        return asContainerApp(null, null, tag, null, dockerfile, 1);
    }

    public JavaAppResource asContainerApp(
        String image,
        String registry,
        String tag,
        String buildContext,
        String dockerfile,
        int replicas) {
        projectAsContainerApp();
        containerApp = true;
        containerImage = image == null || image.isBlank()
            ? createDefaultContainerImage(tag)
            : image;
        containerRegistry = registry;
        containerBuildContext = buildContext == null || buildContext.isBlank()
            ? projectPath
            : buildContext;
        containerDockerfile = dockerfile;
        containerReplicas = replicas <= 0 ? 1 : replicas;
        return this;
    }

    public JavaAppResource withEnvironmentVariable(String name, String value) {
        environment.add(new EnvironmentVariable(name, valueJson(value)));
        return this;
    }

    public JavaAppResource withEnvironmentVariable(
        String name,
        ConfigurationStoreResource.ConfigurationSettingReference reference) {
        environment.add(new EnvironmentVariable(name, configurationSettingReferenceJson(reference)));
        return this;
    }

    public JavaAppResource withEnvironmentVariable(
        String name,
        SecretsVaultResource.SecretReference reference) {
        environment.add(new EnvironmentVariable(name, secretReferenceJson(reference)));
        return this;
    }

    public JavaAppResource withReference(ResourceBuilder<?> resource) {
        references.add(resource);
        return this;
    }

    public JavaAppResource withHttpEndpoint(
        String host,
        int port,
        int targetPort,
        NetworkResource network) {
        endpoint = new EndpointRequest("http", "http", host, port, targetPort, network);
        return this;
    }

    public JavaAppResource withHttpHealthCheck(String path) {
        healthChecks.add(new HealthCheck("health", "health", path));
        return this;
    }

    public JavaAppResource withHttpLivenessCheck(String path) {
        healthChecks.add(new HealthCheck("alive", "liveness", path));
        return this;
    }

    public JavaAppResource withDefaultConsoleLogSource() {
        consoleLogs = true;
        return this;
    }

    @Override
    String toJson(int indent) {
        StringBuilder builder = new StringBuilder();
        line(builder, indent, "{");
        appendCommon(builder, indent + 1);
        property(builder, indent + 1, "command", json("java"), true);
        property(builder, indent + 1, "artifactPath", json(artifactPath), true);
        if (buildTool != null) {
            property(builder, indent + 1, "buildTool", json(buildTool), true);
        }

        if (buildArguments != null) {
            property(builder, indent + 1, "buildArguments", json(buildArguments), true);
        }

        line(builder, indent + 1, "\"project\": {");
        property(builder, indent + 2, "path", json(projectPath), true);
        if (serviceDiscoveryName != null) {
            property(builder, indent + 2, "serviceDiscoveryName", json(serviceDiscoveryName), true);
        }

        appendEnvironmentVariables(builder, indent + 2, true);
        appendReferences(builder, indent + 2, references, false);

        line(builder, indent + 1, "}" + (endpoint != null || containerApp || !healthChecks.isEmpty() || consoleLogs ? "," : ""));
        if (endpoint != null) {
            endpoint.appendJson(builder, indent + 1, containerApp || !healthChecks.isEmpty() || consoleLogs);
        }

        if (containerApp) {
            appendContainerProperties(builder, indent + 1, !healthChecks.isEmpty() || consoleLogs);
        }

        if (!healthChecks.isEmpty()) {
            appendHealth(builder, indent + 1, consoleLogs);
        }

        if (consoleLogs) {
            appendLogs(builder, indent + 1);
        }

        line(builder, indent, "}");
        return builder.toString();
    }

    @Override
    protected JavaAppResource self() {
        return this;
    }

    private void appendContainerProperties(StringBuilder builder, int indent, boolean trailingComma) {
        property(builder, indent, "image", json(containerImage), true);
        property(builder, indent, "replicas", Integer.toString(containerReplicas), containerRegistry != null || containerBuildContext != null || containerDockerfile != null || trailingComma);
        if (containerRegistry != null) {
            property(builder, indent, "registry", json(containerRegistry), containerBuildContext != null || containerDockerfile != null || trailingComma);
        }

        if (containerBuildContext != null) {
            property(builder, indent, "buildContext", json(containerBuildContext), containerDockerfile != null || trailingComma);
        }

        if (containerDockerfile != null) {
            property(builder, indent, "dockerfile", json(containerDockerfile), trailingComma);
        }
    }

    private String createDefaultContainerImage(String tag) {
        String normalized = name()
            .trim()
            .toLowerCase()
            .replaceAll("[^a-z0-9]", "-")
            .replaceAll("^-+|-+$", "");
        if (normalized.isBlank()) {
            normalized = "app";
        }

        return "cloudshell-java-" + normalized + ":" + (tag == null || tag.isBlank() ? "dev" : tag.trim());
    }

    private void appendHealth(StringBuilder builder, int indent, boolean trailingComma) {
        line(builder, indent, "\"health\": {");
        line(builder, indent + 1, "\"checks\": [");
        for (int index = 0; index < healthChecks.size(); index++) {
            HealthCheck check = healthChecks.get(index);
            line(builder, indent + 2, "{");
            property(builder, indent + 3, "name", json(check.name()), true);
            property(builder, indent + 3, "type", json(check.type()), true);
            line(builder, indent + 3, "\"source\": {");
            property(builder, indent + 4, "kind", json("http"), true);
            line(builder, indent + 4, "\"http\": {");
            property(builder, indent + 5, "path", json(check.path()), true);
            property(builder, indent + 5, "endpointName", json("http"), false);
            line(builder, indent + 4, "}");
            line(builder, indent + 3, "}");
            line(builder, indent + 2, "}" + (index < healthChecks.size() - 1 ? "," : ""));
        }

        line(builder, indent + 1, "]");
        line(builder, indent, "}" + (trailingComma ? "," : ""));
    }

    private void appendLogs(StringBuilder builder, int indent) {
        line(builder, indent, "\"logs\": {");
        line(builder, indent + 1, "\"sources\": [");
        line(builder, indent + 2, "{");
        property(builder, indent + 3, "id", json("console"), true);
        property(builder, indent + 3, "name", json("Console logs"), true);
        property(builder, indent + 3, "kind", json("processOutput"), true);
        property(builder, indent + 3, "format", json("plainText"), true);
        line(builder, indent + 3, "\"capabilities\": [\"read\", \"stream\"],");
        property(builder, indent + 3, "description", json("Provider-captured process console output."), true);
        property(builder, indent + 3, "origin", json("providerDefault"), true);
        property(builder, indent + 3, "purpose", json("default"), true);
        property(builder, indent + 3, "availability", json("resourceRunning"), false);
        line(builder, indent + 2, "}");
        line(builder, indent + 1, "]");
        line(builder, indent, "}");
    }

    private void appendEnvironmentVariables(StringBuilder builder, int indent, boolean trailingComma) {
        line(builder, indent, "\"environmentVariables\": {");
        for (int index = 0; index < environment.size(); index++) {
            EnvironmentVariable variable = environment.get(index);
            line(builder, indent + 1, json(variable.name()) + ": " + variable.valueJson()
                + (index < environment.size() - 1 ? "," : ""));
        }

        line(builder, indent, "}" + (trailingComma ? "," : ""));
    }

    private static String valueJson(String value) {
        return "{ \"value\": " + json(value) + " }";
    }

    private static String configurationSettingReferenceJson(
        ConfigurationStoreResource.ConfigurationSettingReference reference) {
        StringBuilder builder = new StringBuilder();
        builder.append("{ \"configurationSettingRef\": { ");
        builder.append("\"storeResourceId\": ").append(json(reference.storeResourceId())).append(", ");
        builder.append("\"name\": ").append(json(reference.name()));
        if (reference.version() != null) {
            builder.append(", \"version\": ").append(json(reference.version()));
        }

        builder.append(" } }");
        return builder.toString();
    }

    private static String secretReferenceJson(SecretsVaultResource.SecretReference reference) {
        StringBuilder builder = new StringBuilder();
        builder.append("{ \"secretRef\": { ");
        builder.append("\"vaultResourceId\": ").append(json(reference.vaultResourceId())).append(", ");
        builder.append("\"name\": ").append(json(reference.name()));
        if (reference.version() != null) {
            builder.append(", \"version\": ").append(json(reference.version()));
        }

        builder.append(" } }");
        return builder.toString();
    }

    private record HealthCheck(String name, String type, String path) {
    }

    private record EnvironmentVariable(String name, String valueJson) {
    }
}
