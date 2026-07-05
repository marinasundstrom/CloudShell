package com.cloudshell.sdk;

public final class DefaultCloudShellTokenCredential implements CloudShellTokenCredential {
    private final CloudShellTokenCredential[] credentials;

    public DefaultCloudShellTokenCredential() {
        this(new IdentityTokenCredential(), new EnvironmentTokenCredential(), new ProfileTokenCredential());
    }

    public DefaultCloudShellTokenCredential(CloudShellTokenCredential... credentials) {
        this.credentials = credentials;
    }

    @Override
    public String getToken(String[] scopes) {
        for (CloudShellTokenCredential credential : credentials) {
            String token = credential.getToken(scopes);
            if (token != null && !token.isBlank()) {
                return token.trim();
            }
        }

        return null;
    }
}
