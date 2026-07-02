package com.cloudshell.sdk;

import java.net.URI;
import java.util.Comparator;
import java.util.Map;
import java.util.Optional;

final class CloudShellEnvironment {
    private CloudShellEnvironment() {
    }

    static Optional<URI> findEndpoint(
        String prefix,
        String serviceName,
        Map<String, String> environment) {
        String normalizedServiceName = serviceName == null || serviceName.isBlank()
            ? null
            : normalizeEnvironmentSegment(serviceName);

        return environment.entrySet().stream()
            .filter(entry -> entry.getValue() != null && !entry.getValue().isBlank())
            .filter(entry -> {
                String key = entry.getKey().toUpperCase();
                return key.startsWith(prefix) &&
                    key.endsWith("_ENDPOINT") &&
                    (normalizedServiceName == null || key.contains(prefix + normalizedServiceName + "_"));
            })
            .sorted(Comparator.comparing(Map.Entry::getKey, String.CASE_INSENSITIVE_ORDER))
            .map(Map.Entry::getValue)
            .map(CloudShellEnvironment::tryCreateUri)
            .filter(Optional::isPresent)
            .map(Optional::get)
            .findFirst();
    }

    static String normalizeEnvironmentSegment(String value) {
        StringBuilder builder = new StringBuilder();
        for (char character : value.trim().toCharArray()) {
            builder.append(Character.isLetterOrDigit(character)
                ? Character.toUpperCase(character)
                : '_');
        }

        return builder.toString().replaceAll("^_+|_+$", "");
    }

    private static Optional<URI> tryCreateUri(String value) {
        try {
            URI uri = URI.create(value);
            return uri.isAbsolute() ? Optional.of(uri) : Optional.empty();
        } catch (IllegalArgumentException exception) {
            return Optional.empty();
        }
    }
}
