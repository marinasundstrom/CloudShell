package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

import java.util.ArrayList;
import java.util.function.Consumer;
import java.util.List;

public final class ConfigurationStoreResource extends ResourceBuilder<ConfigurationStoreResource> {
    private String endpoint;
    private final List<ConfigurationSeedSetting> settings = new ArrayList<>();

    ConfigurationStoreResource(String name) {
        super(name, "configuration.store", "configuration");
    }

    public ConfigurationStoreResource withEndpoint(String endpoint) {
        this.endpoint = endpoint;
        return this;
    }

    public ConfigurationStoreResource withSeed(Consumer<ConfigurationStoreSeed> configure) {
        ConfigurationStoreSeed seed = new ConfigurationStoreSeed();
        configure.accept(seed);
        this.settings.clear();
        this.settings.addAll(seed.settings);
        return this;
    }

    public ConfigurationSettingReference setting(String name) {
        return setting(name, null);
    }

    public ConfigurationSettingReference setting(String name, String version) {
        return new ConfigurationSettingReference(resourceId(), name, version);
    }

    @Override
    String toJson(int indent) {
        StringBuilder builder = new StringBuilder();
        line(builder, indent, "{");
        appendCommon(builder, indent + 1);
        property(builder, indent + 1, "endpoint", json(endpoint), !settings.isEmpty());
        if (!settings.isEmpty()) {
            line(builder, indent + 1, "\"seed\": {");
            appendSettings(builder, indent + 2);
            line(builder, indent + 1, "}");
        }

        line(builder, indent, "}");
        return builder.toString();
    }

    private void appendSettings(StringBuilder builder, int indent) {
        line(builder, indent, "\"settings\": [");
        for (int index = 0; index < settings.size(); index++) {
            ConfigurationSeedSetting setting = settings.get(index);
            line(builder, indent + 1, "{");
            property(builder, indent + 2, "name", json(setting.name()), true);
            property(builder, indent + 2, "value", json(setting.value()), false);
            line(builder, indent + 1, "}" + (index < settings.size() - 1 ? "," : ""));
        }

        line(builder, indent, "]");
    }

    @Override
    protected ConfigurationStoreResource self() {
        return this;
    }

    public static final class ConfigurationStoreSeed {
        private final List<ConfigurationSeedSetting> settings = new ArrayList<>();

        public ConfigurationStoreSeed setting(String name, String value) {
            settings.add(new ConfigurationSeedSetting(name, value));
            return this;
        }
    }

    public record ConfigurationSeedSetting(String name, String value) {
    }

    public record ConfigurationSettingReference(String storeResourceId, String name, String version) {
    }
}
