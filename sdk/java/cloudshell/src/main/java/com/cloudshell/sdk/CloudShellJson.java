package com.cloudshell.sdk;

import java.util.ArrayList;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

final class CloudShellJson {
    private CloudShellJson() {
    }

    static List<String> objects(String json) {
        List<String> result = new ArrayList<>();
        int depth = 0;
        int start = -1;
        boolean inString = false;
        boolean escaped = false;
        for (int index = 0; index < json.length(); index++) {
            char character = json.charAt(index);
            if (escaped) {
                escaped = false;
                continue;
            }

            if (character == '\\' && inString) {
                escaped = true;
                continue;
            }

            if (character == '"') {
                inString = !inString;
                continue;
            }

            if (inString) {
                continue;
            }

            if (character == '{') {
                if (depth == 0) {
                    start = index;
                }

                depth++;
            } else if (character == '}') {
                depth--;
                if (depth == 0 && start >= 0) {
                    result.add(json.substring(start, index + 1));
                }
            }
        }

        return result;
    }

    static String stringProperty(String json, String name) {
        Pattern pattern = Pattern.compile(
            "\"" + Pattern.quote(name) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
        Matcher matcher = pattern.matcher(json);
        return matcher.find() ? unescape(matcher.group(1)) : null;
    }

    static String objectProperty(String json, String name) {
        return objectProperty(json, name, false);
    }

    static String objectPropertyIgnoreCase(String json, String name) {
        return objectProperty(json, name, true);
    }

    static boolean booleanProperty(String json, String name) {
        Pattern pattern = Pattern.compile(
            "\"" + Pattern.quote(name) + "\"\\s*:\\s*(true|false)");
        Matcher matcher = pattern.matcher(json);
        return matcher.find() && Boolean.parseBoolean(matcher.group(1));
    }

    static String numberProperty(String json, String name) {
        Pattern pattern = Pattern.compile(
            "\"" + Pattern.quote(name) + "\"\\s*:\\s*(-?\\d+)");
        Matcher matcher = pattern.matcher(json);
        return matcher.find() ? matcher.group(1) : null;
    }

    private static String objectProperty(String json, String name, boolean ignoreCase) {
        Pattern pattern = Pattern.compile("\"((?:\\\\.|[^\"])*)\"\\s*:\\s*\\{");
        Matcher matcher = pattern.matcher(json);
        while (matcher.find()) {
            String candidateName = unescape(matcher.group(1));
            boolean matches = ignoreCase
                ? candidateName.equalsIgnoreCase(name)
                : candidateName.equals(name);
            if (matches) {
                return extractObject(json, matcher.end() - 1);
            }
        }

        return null;
    }

    private static String extractObject(String json, int openBraceIndex) {
        int depth = 0;
        boolean inString = false;
        boolean escaped = false;
        for (int index = openBraceIndex; index < json.length(); index++) {
            char character = json.charAt(index);
            if (escaped) {
                escaped = false;
                continue;
            }

            if (character == '\\' && inString) {
                escaped = true;
                continue;
            }

            if (character == '"') {
                inString = !inString;
                continue;
            }

            if (inString) {
                continue;
            }

            if (character == '{') {
                depth++;
            } else if (character == '}') {
                depth--;
                if (depth == 0) {
                    return json.substring(openBraceIndex, index + 1);
                }
            }
        }

        return null;
    }

    private static String unescape(String value) {
        StringBuilder builder = new StringBuilder();
        boolean escaped = false;
        for (int index = 0; index < value.length(); index++) {
            char character = value.charAt(index);
            if (escaped) {
                builder.append(switch (character) {
                    case 'n' -> '\n';
                    case 'r' -> '\r';
                    case 't' -> '\t';
                    case '"', '\\', '/' -> character;
                    default -> character;
                });
                escaped = false;
            } else if (character == '\\') {
                escaped = true;
            } else {
                builder.append(character);
            }
        }

        return builder.toString();
    }
}
