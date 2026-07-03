package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

import java.util.ArrayList;
import java.util.List;

public final class ConfigurationStoreResource extends ResourceBuilder<ConfigurationStoreResource> {
    private String endpoint;
    private final List<ConfigurationSettingEntry> entries = new ArrayList<>();

    ConfigurationStoreResource(String name) {
        super(name, "configuration.store", "configuration");
    }

    public ConfigurationStoreResource withEndpoint(String endpoint) {
        this.endpoint = endpoint;
        return this;
    }

    public ConfigurationStoreResource withEntry(String name, String value) {
        return withSetting(name, value);
    }

    public ConfigurationStoreResource withSetting(String name, String value) {
        entries.add(new ConfigurationSettingEntry(name, value));
        return this;
    }

    public ConfigurationStoreResource withEntries(List<ConfigurationSeedEntry> entries) {
        return withSettings(entries.stream()
                .map(entry -> new ConfigurationSettingEntry(entry.name(), entry.value()))
                .toList());
    }

    public ConfigurationStoreResource withSettings(List<ConfigurationSettingEntry> entries) {
        this.entries.clear();
        this.entries.addAll(entries);
        return this;
    }

    public ConfigurationEntryReference entry(String name) {
        return entry(name, null);
    }

    public ConfigurationEntryReference entry(String name, String version) {
        return new ConfigurationEntryReference(resourceId(), name, version);
    }

    @Override
    String toJson(int indent) {
        StringBuilder builder = new StringBuilder();
        line(builder, indent, "{");
        appendCommon(builder, indent + 1);
        property(builder, indent + 1, "endpoint", json(endpoint), !entries.isEmpty());
        if (!entries.isEmpty()) {
            line(builder, indent + 1, "\"seed\": {");
            appendEntries(builder, indent + 2);
            line(builder, indent + 1, "}");
        }

        line(builder, indent, "}");
        return builder.toString();
    }

    private void appendEntries(StringBuilder builder, int indent) {
        line(builder, indent, "\"entries\": [");
        for (int index = 0; index < entries.size(); index++) {
            ConfigurationSettingEntry entry = entries.get(index);
            line(builder, indent + 1, "{");
            property(builder, indent + 2, "name", json(entry.name()), true);
            property(builder, indent + 2, "value", json(entry.value()), false);
            line(builder, indent + 1, "}" + (index < entries.size() - 1 ? "," : ""));
        }

        line(builder, indent, "]");
    }

    @Override
    protected ConfigurationStoreResource self() {
        return this;
    }

    public record ConfigurationSeedEntry(String name, String value) {
    }

    public record ConfigurationSettingEntry(String name, String value) {
    }

    public record ConfigurationEntryReference(String storeResourceId, String name, String version) {
    }
}
