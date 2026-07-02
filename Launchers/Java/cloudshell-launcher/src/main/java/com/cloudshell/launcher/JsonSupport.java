package com.cloudshell.launcher;

import java.util.List;

final class JsonSupport {
    private JsonSupport() {
    }

    static String json(String value) {
        if (value == null) {
            return "null";
        }

        return "\"" + value
            .replace("\\", "\\\\")
            .replace("\"", "\\\"")
            .replace("\n", "\\n")
            .replace("\r", "\\r") + "\"";
    }

    static void property(
        StringBuilder builder,
        int indent,
        String name,
        String value,
        boolean trailingComma) {
        line(builder, indent, json(name) + ": " + value + (trailingComma ? "," : ""));
    }

    static void object(
        StringBuilder builder,
        int indent,
        String name,
        List<NameValue> values,
        boolean trailingComma) {
        line(builder, indent, json(name) + ": {");
        for (int index = 0; index < values.size(); index++) {
            NameValue pair = values.get(index);
            property(builder, indent + 1, pair.name(), json(pair.value()), index < values.size() - 1);
        }

        line(builder, indent, "}" + (trailingComma ? "," : ""));
    }

    static void objectOfValueObjects(
        StringBuilder builder,
        int indent,
        String name,
        List<NameValue> values,
        boolean trailingComma) {
        line(builder, indent, json(name) + ": {");
        for (int index = 0; index < values.size(); index++) {
            NameValue pair = values.get(index);
            line(builder, indent + 1, json(pair.name()) + ": {");
            property(builder, indent + 2, "value", json(pair.value()), false);
            line(builder, indent + 1, "}" + (index < values.size() - 1 ? "," : ""));
        }

        line(builder, indent, "}" + (trailingComma ? "," : ""));
    }

    static void appendReferences(
        StringBuilder builder,
        int indent,
        List<? extends ResourceBuilder<?>> references,
        boolean trailingComma) {
        line(builder, indent, json("references") + ": [");
        for (int index = 0; index < references.size(); index++) {
            ResourceBuilder<?> reference = references.get(index);
            appendReferenceObject(
                builder,
                indent + 1,
                new ResourceReferenceValue(
                    reference.resourceId(),
                    "reference",
                    "resourceId",
                    reference.type(),
                    reference.providerId()),
                index < references.size() - 1);
        }

        line(builder, indent, "]" + (trailingComma ? "," : ""));
    }

    static void appendResourceReferences(
        StringBuilder builder,
        int indent,
        String name,
        List<ResourceReferenceValue> references,
        boolean trailingComma) {
        line(builder, indent, json(name) + ": [");
        for (int index = 0; index < references.size(); index++) {
            appendReferenceObject(
                builder,
                indent + 1,
                references.get(index),
                index < references.size() - 1);
        }

        line(builder, indent, "]" + (trailingComma ? "," : ""));
    }

    static ResourceReferenceValue dependsOn(ResourceBuilder<?> resource) {
        return new ResourceReferenceValue(
            resource.resourceId(),
            "dependsOn",
            "resourceId",
            resource.type(),
            resource.providerId());
    }

    static ResourceReferenceValue dependsOn(String resourceId) {
        return new ResourceReferenceValue(
            resourceId,
            "dependsOn",
            "resourceId",
            null,
            null);
    }

    private static void appendReferenceObject(
        StringBuilder builder,
        int indent,
        ResourceReferenceValue reference,
        boolean trailingComma) {
        line(builder, indent, "{");
        property(builder, indent + 1, "resourceId", json(reference.resourceId()), true);
        property(builder, indent + 1, "relationship", json(reference.relationship()), true);
        property(
            builder,
            indent + 1,
            "addressingMode",
            json(reference.addressingMode()),
            reference.typeId() != null || reference.providerId() != null);
        if (reference.typeId() != null) {
            property(builder, indent + 1, "typeId", json(reference.typeId()), reference.providerId() != null);
        }

        if (reference.providerId() != null) {
            property(builder, indent + 1, "providerId", json(reference.providerId()), false);
        }

        line(builder, indent, "}" + (trailingComma ? "," : ""));
    }

    static void line(StringBuilder builder, int indent, String value) {
        String prefix = "  ".repeat(indent);
        String[] lines = value.split("\\R", -1);
        for (String line : lines) {
            if (!line.isEmpty()) {
                builder.append(prefix).append(line);
            }

            builder.append('\n');
        }
    }

    record NameValue(String name, String value) {
    }

    record ResourceReferenceValue(
        String resourceId,
        String relationship,
        String addressingMode,
        String typeId,
        String providerId) {
    }
}
