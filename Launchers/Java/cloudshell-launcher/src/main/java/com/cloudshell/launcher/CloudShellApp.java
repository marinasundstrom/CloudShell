package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.object;
import static com.cloudshell.launcher.JsonSupport.property;

import com.cloudshell.launcher.JsonSupport.NameValue;
import java.util.ArrayList;
import java.util.List;

public final class CloudShellApp {
    private final String name;
    private String environmentId;
    private final List<NameValue> metadata = new ArrayList<>();
    private final List<ResourceBuilder<?>> resources = new ArrayList<>();

    private CloudShellApp(String name) {
        this.name = name;
    }

    public static CloudShellApp create(String name) {
        return new CloudShellApp(name);
    }

    public CloudShellApp withEnvironmentId(String environmentId) {
        this.environmentId = environmentId;
        return this;
    }

    public CloudShellApp withMetadata(String name, String value) {
        metadata.add(new NameValue(name, value));
        return this;
    }

    public NetworkResource addNetwork(String name) {
        NetworkResource resource = new NetworkResource(name);
        resources.add(resource);
        return resource;
    }

    public JavaAppResource addJavaApp(String name, String projectPath, String artifactPath) {
        JavaAppResource resource = new JavaAppResource(name, projectPath, artifactPath);
        resources.add(resource);
        return resource;
    }

    public ConfigurationStoreResource addConfigurationStore(String name) {
        ConfigurationStoreResource resource = new ConfigurationStoreResource(name);
        resources.add(resource);
        return resource;
    }

    public SecretsVaultResource addSecretsVault(String name) {
        SecretsVaultResource resource = new SecretsVaultResource(name);
        resources.add(resource);
        return resource;
    }

    public String toJson() {
        StringBuilder builder = new StringBuilder();
        line(builder, 0, "{");
        property(builder, 1, "name", json(name), true);
        property(builder, 1, "environmentId", json(environmentId), true);
        object(builder, 1, "metadata", metadata, true);
        line(builder, 1, "\"resources\": [");
        for (int index = 0; index < resources.size(); index++) {
            builder.append(resources.get(index).toJson(2));
            builder.append(index == resources.size() - 1 ? "\n" : ",\n");
        }

        line(builder, 1, "]");
        line(builder, 0, "}");
        return builder.toString();
    }
}
