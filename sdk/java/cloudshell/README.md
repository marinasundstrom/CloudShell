# CloudShell Java SDK

This experimental Java SDK provides CloudShell service clients for Java
workloads. The first clients cover Configuration Store and Secrets Vault.

This package is for running Java applications that consume CloudShell-managed
services. Java ResourceTemplate/app-host builders live in the separate
`Launchers/Java/cloudshell-launcher` package so launcher authoring stays
separate from runtime service clients.

The clients discover service bindings from the same environment variables used
by other language integrations:

```text
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT
CLOUDSHELL_SECRETS_<VAULT_NAME>_ENDPOINT
CLOUDSHELL_CONFIGURATION_TOKEN
CLOUDSHELL_SECRETS_TOKEN
CLOUDSHELL_CONTROL_PLANE_TOKEN
CLOUDSHELL_TOKEN
CLOUDSHELL_CONFIG_DIR
CLOUDSHELL_PROFILE
```

Example:

```java
ConfigurationStoreClient configuration =
    ConfigurationStoreClient.fromEnvironment();

String message = configuration
    .getSetting("Sample--Message")
    .map(CloudShellConfigurationSetting::value)
    .orElse("Default message");

SecretsVaultClient secrets = SecretsVaultClient.fromEnvironment();
String secret = secrets
    .getSecret("Sample--Secret")
    .map(SecretValue::value)
    .orElse("");
```

By default, clients use `DefaultCloudShellTokenCredential`. It checks the
environment token variables first, then reads the active CloudShell profile from
`~/.cloudshell/config.json`. `CLOUDSHELL_CONFIG_DIR` overrides the profile
directory, and `CLOUDSHELL_PROFILE` selects a named profile. The first supported
profile credential kind is `staticBearer`, using either `accessToken` for
short-lived tests or `accessTokenPath` for a local token file relative to the
profile directory.

For framework integration, `ConfigurationStoreClient.toProperties(true)` maps
portable CloudShell hierarchy names such as `Sample--Message` to Java-style
property names such as `Sample.Message`.
