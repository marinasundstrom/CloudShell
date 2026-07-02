# CloudShell Java SDK

This experimental Java SDK provides CloudShell service clients for Java
workloads. The first clients cover Configuration Store and Secrets Vault.

This package is for running Java applications that consume CloudShell-managed
services. Java ResourceTemplate/app-host builders remain sample-local in
`samples/JavaAppHost` until CloudShell introduces a separate Java app-host
authoring package.

The clients discover service bindings from the same environment variables used
by other language integrations:

```text
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT
CLOUDSHELL_SECRETS_<VAULT_NAME>_ENDPOINT
CLOUDSHELL_CONFIGURATION_TOKEN
CLOUDSHELL_SECRETS_TOKEN
CLOUDSHELL_CONTROL_PLANE_TOKEN
CLOUDSHELL_TOKEN
```

Example:

```java
ConfigurationStoreClient configuration =
    ConfigurationStoreClient.fromEnvironment();

String message = configuration
    .getEntry("Sample--Message")
    .map(CloudShellConfigurationEntry::value)
    .orElse("Default message");

SecretsVaultClient secrets = SecretsVaultClient.fromEnvironment();
String secret = secrets
    .getSecret("Sample--Secret")
    .map(SecretValue::value)
    .orElse("");
```

For framework integration, `ConfigurationStoreClient.toProperties(true)` maps
portable CloudShell hierarchy names such as `Sample--Message` to Java-style
property names such as `Sample.Message`.
