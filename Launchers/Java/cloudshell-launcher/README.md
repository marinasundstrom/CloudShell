# CloudShell Java Launcher SDK

This experimental package provides Java-native builders for authoring
CloudShell ResourceTemplate documents from a Java launcher app.

The launcher SDK is separate from `sdk/java/cloudshell`. Runtime applications
use `sdk/java/cloudshell` to consume Configuration Store and Secrets Vault
bindings after CloudShell starts them. Launcher applications use this package
to declare resources, emit ResourceTemplate JSON, and hand that template to
the CloudShell CLI or Control Plane.

The first supported surface is intentionally small:

- `CloudShellApp`
- `NetworkResource`
- `JavaAppResource`
- `ConfigurationStoreResource`
- `SecretsVaultResource`

The API uses ordinary Java classes and fluent methods instead of C# extension
method patterns. The stable interoperability boundary remains the
ResourceTemplate JSON shape.

CLI apply/start helpers are still exercised from `samples/JavaAppHost` until
the Java process orchestration API is proven across more scenarios.

Compile the package sources directly:

```bash
javac -d target/classes $(find src/main/java -name '*.java')
```

Run the package tests:

```bash
./test.sh
```
