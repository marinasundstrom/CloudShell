package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

public final class ConfigurationStoreResource extends ResourceBuilder<ConfigurationStoreResource> {
    private String endpoint;

    ConfigurationStoreResource(String name) {
        super(name, "configuration.store", "configuration");
    }

    public ConfigurationStoreResource withEndpoint(String endpoint) {
        this.endpoint = endpoint;
        return this;
    }

    @Override
    String toJson(int indent) {
        StringBuilder builder = new StringBuilder();
        line(builder, indent, "{");
        appendCommon(builder, indent + 1);
        line(builder, indent + 1, "\"attributes\": {");
        line(builder, indent + 2, "\"configuration\": {");
        property(builder, indent + 3, "endpoint", json(endpoint), false);
        line(builder, indent + 2, "}");
        line(builder, indent + 1, "}");
        line(builder, indent, "}");
        return builder.toString();
    }

    @Override
    protected ConfigurationStoreResource self() {
        return this;
    }
}
