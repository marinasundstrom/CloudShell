package com.cloudshell.sdk;

public final class StaticTokenCredential implements CloudShellTokenCredential {
    private final String token;

    public StaticTokenCredential(String token) {
        this.token = token;
    }

    @Override
    public String getToken(String[] scopes) {
        return token;
    }
}
