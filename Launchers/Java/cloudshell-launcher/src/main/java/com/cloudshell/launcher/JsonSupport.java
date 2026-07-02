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
        line(builder, indent, "\"references\": [");
        for (int index = 0; index < references.size(); index++) {
            ResourceBuilder<?> reference = references.get(index);
            line(builder, indent + 1, "{");
            property(builder, indent + 2, "resourceId", json(reference.resourceId()), true);
            property(builder, indent + 2, "relationship", json("reference"), true);
            property(builder, indent + 2, "addressingMode", json("resourceId"), true);
            property(builder, indent + 2, "typeId", json(reference.type()), true);
            property(builder, indent + 2, "providerId", json(reference.providerId()), false);
            line(builder, indent + 1, "}" + (index < references.size() - 1 ? "," : ""));
        }

        line(builder, indent, "]" + (trailingComma ? "," : ""));
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
}
