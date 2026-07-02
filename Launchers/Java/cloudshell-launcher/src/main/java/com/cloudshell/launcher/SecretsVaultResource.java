package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

public final class SecretsVaultResource extends ResourceBuilder<SecretsVaultResource> {
    private String endpoint;

    SecretsVaultResource(String name) {
        super(name, "secrets.vault", "secrets-vault");
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
        line(builder, indent + 1, "\"attributes\": {");
        line(builder, indent + 2, "\"secrets\": {");
        property(builder, indent + 3, "endpoint", json(endpoint), false);
        line(builder, indent + 2, "}");
        line(builder, indent + 1, "}");
        line(builder, indent, "}");
        return builder.toString();
    }

    @Override
    protected SecretsVaultResource self() {
        return this;
    }
}
