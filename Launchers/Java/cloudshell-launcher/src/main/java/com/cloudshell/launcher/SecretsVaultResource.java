package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

public final class SecretsVaultResource extends ResourceBuilder<SecretsVaultResource> {
    private String endpoint;

    SecretsVaultResource(String name) {
        super(name, "secrets.vault", "secrets");
    }

    public SecretsVaultResource withEndpoint(String endpoint) {
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

    @Override
    protected SecretsVaultResource self() {
        return this;
    }
}
