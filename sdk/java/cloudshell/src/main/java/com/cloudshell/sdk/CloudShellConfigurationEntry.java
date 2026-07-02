package com.cloudshell.sdk;

public record CloudShellConfigurationEntry(
    String name,
    String value,
    boolean secret) {
}
