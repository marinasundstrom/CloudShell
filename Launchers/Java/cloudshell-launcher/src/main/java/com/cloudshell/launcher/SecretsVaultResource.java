package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

import java.util.ArrayList;
import java.util.List;

public final class SecretsVaultResource extends ResourceBuilder<SecretsVaultResource> {
    private String endpoint;
    private final List<SecretSeedValue> secrets = new ArrayList<>();
    private final List<CertificateSeedValue> certificates = new ArrayList<>();

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

    public SecretsVaultResource withCertificate(String name, String value) {
        return withCertificate(name, value, null, null);
    }

    public SecretsVaultResource withCertificate(String name, String value, String version) {
        return withCertificate(name, value, version, null);
    }

    public SecretsVaultResource withCertificate(String name, String value, String version, String contentType) {
        certificates.add(new CertificateSeedValue(name, value, version, contentType));
        return this;
    }

    public SecretsVaultResource withCertificates(List<CertificateSeedValue> certificates) {
        this.certificates.clear();
        this.certificates.addAll(certificates);
        return this;
    }

    public SecretReference secret(String name) {
        return secret(name, null);
    }

    public SecretReference secret(String name, String version) {
        return new SecretReference(resourceId(), name, version);
    }

    public CertificateReference certificate(String name) {
        return certificate(name, null);
    }

    public CertificateReference certificate(String name, String version) {
        return new CertificateReference(resourceId(), name, version);
    }

    @Override
    String toJson(int indent) {
        StringBuilder builder = new StringBuilder();
        line(builder, indent, "{");
        appendCommon(builder, indent + 1);
        boolean hasSeed = !secrets.isEmpty() || !certificates.isEmpty();
        property(builder, indent + 1, "endpoint", json(endpoint), hasSeed);
        if (hasSeed) {
            line(builder, indent + 1, "\"seed\": {");
            if (!secrets.isEmpty()) {
                appendSecrets(builder, indent + 2, !certificates.isEmpty());
            }

            if (!certificates.isEmpty()) {
                appendCertificates(builder, indent + 2);
            }
            line(builder, indent + 1, "}");
        }

        line(builder, indent, "}");
        return builder.toString();
    }

    private void appendSecrets(StringBuilder builder, int indent, boolean trailingComma) {
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

        line(builder, indent, "]" + (trailingComma ? "," : ""));
    }

    private void appendCertificates(StringBuilder builder, int indent) {
        line(builder, indent, "\"certificates\": [");
        for (int index = 0; index < certificates.size(); index++) {
            CertificateSeedValue certificate = certificates.get(index);
            line(builder, indent + 1, "{");
            property(builder, indent + 2, "name", json(certificate.name()), true);
            property(builder, indent + 2, "value", json(certificate.value()),
                certificate.version() != null || certificate.contentType() != null);
            if (certificate.version() != null) {
                property(builder, indent + 2, "version", json(certificate.version()), certificate.contentType() != null);
            }

            if (certificate.contentType() != null) {
                property(builder, indent + 2, "contentType", json(certificate.contentType()), false);
            }

            line(builder, indent + 1, "}" + (index < certificates.size() - 1 ? "," : ""));
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

    public record CertificateSeedValue(String name, String value, String version, String contentType) {
    }

    public record CertificateReference(String vaultResourceId, String name, String version) {
    }
}
