package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

import java.util.ArrayList;
import java.util.List;

public final class SecretsVaultResource extends ResourceBuilder<SecretsVaultResource> {
    private String endpoint;
    private final List<SecretSeedValue> secrets = new ArrayList<>();

    SecretsVaultResource(String name) {
        super(name, "secrets.vault", "secrets-vault");
    }

    public SecretsVaultResource withEndpoint(String endpoint) {
        this.endpoint = endpoint;
        return this;
    }

    public SecretsVaultResource withSecret(String name, String value) {
        return withSecret(name, value, null);
    }

    public SecretsVaultResource withSecret(String name, String value, String version) {
        secrets.add(new SecretSeedValue(name, value, version));
        return this;
    }

    public SecretsVaultResource withSecrets(List<SecretSeedValue> secrets) {
        this.secrets.clear();
        this.secrets.addAll(secrets);
        return this;
    }

    public SecretReference secret(String name) {
        return secret(name, null);
    }

    public SecretReference secret(String name, String version) {
        return new SecretReference(resourceId(), name, version);
    }

    @Override
    String toJson(int indent) {
        StringBuilder builder = new StringBuilder();
        line(builder, indent, "{");
        appendCommon(builder, indent + 1);
        property(builder, indent + 1, "endpoint", json(endpoint), !secrets.isEmpty());
        if (!secrets.isEmpty()) {
            line(builder, indent + 1, "\"seed\": {");
            appendSecrets(builder, indent + 2);
            line(builder, indent + 1, "}");
        }

        line(builder, indent, "}");
        return builder.toString();
    }

    private void appendSecrets(StringBuilder builder, int indent) {
        line(builder, indent, "\"secrets\": [");
        for (int index = 0; index < secrets.size(); index++) {
            SecretSeedValue secret = secrets.get(index);
            line(builder, indent + 1, "{");
            property(builder, indent + 2, "name", json(secret.name()), true);
            property(builder, indent + 2, "value", json(secret.value()), secret.version() != null);
            if (secret.version() != null) {
                property(builder, indent + 2, "version", json(secret.version()), false);
            }

            line(builder, indent + 1, "}" + (index < secrets.size() - 1 ? "," : ""));
        }

        line(builder, indent, "]");
    }

    @Override
    protected SecretsVaultResource self() {
        return this;
    }

    public record SecretSeedValue(String name, String value, String version) {
    }

    public record SecretReference(String vaultResourceId, String name, String version) {
    }
}
