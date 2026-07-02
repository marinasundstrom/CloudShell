package com.cloudshell.sdk;

import java.util.Map;

public final class EnvironmentTokenCredential implements CloudShellTokenCredential {
    private final String[] variableNames;
    private final Map<String, String> environment;

    public EnvironmentTokenCredential(String... variableNames) {
        this(variableNames, System.getenv());
    }

    public EnvironmentTokenCredential(String[] variableNames, Map<String, String> environment) {
        this.variableNames = variableNames.length == 0
            ? new String[] {
                "CLOUDSHELL_CONFIGURATION_TOKEN",
                "CLOUDSHELL_SECRETS_TOKEN",
                "CLOUDSHELL_CONTROL_PLANE_TOKEN",
                "CLOUDSHELL_TOKEN"
            }
            : variableNames;
        this.environment = environment;
    }

    @Override
    public String getToken(String[] scopes) {
        for (String variableName : variableNames) {
            String token = environment.get(variableName);
            if (token != null && !token.isBlank()) {
                return token.trim();
            }
        }

        return null;
    }
}
