package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.property;

public abstract class ResourceBuilder<T extends ResourceBuilder<T>> {
    private final String name;
    private final String type;
    private final String providerId;
    private String resourceId;
    private String displayName;

    protected ResourceBuilder(String name, String type, String providerId) {
        this.name = name;
        this.type = type;
        this.providerId = providerId;
        this.resourceId = type + ":" + name;
    }

    public final String name() {
        return name;
    }

    public final String type() {
        return type;
    }

    public final String providerId() {
        return providerId;
    }

    public final String resourceId() {
        return resourceId;
    }

    public final String displayName() {
        return displayName;
    }

    public T withResourceId(String resourceId) {
        this.resourceId = resourceId;
        return self();
    }

    public T withDisplayName(String displayName) {
        this.displayName = displayName;
        return self();
    }

    abstract String toJson(int indent);

    protected abstract T self();

    protected final void appendCommon(StringBuilder builder, int indent) {
        property(builder, indent, "name", json(name), true);
        property(builder, indent, "type", json(type), true);
        property(builder, indent, "resourceId", json(resourceId), true);
        property(builder, indent, "providerId", json(providerId), true);
        if (displayName != null) {
            property(builder, indent, "displayName", json(displayName), true);
        }
    }
}
