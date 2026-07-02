package com.cloudshell.sdk;

public record SecretValue(
    String name,
    String value,
    String version) {
}
