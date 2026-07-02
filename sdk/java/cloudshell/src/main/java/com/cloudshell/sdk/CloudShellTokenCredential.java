package com.cloudshell.sdk;

public interface CloudShellTokenCredential {
    String getToken(String[] scopes);
}
