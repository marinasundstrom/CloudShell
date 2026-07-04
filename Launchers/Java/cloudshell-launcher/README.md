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
- `CloudShellLauncherOptions`
- `CloudShellApp.apply(...)`
- `CloudShellApp.start(...)`
- `CloudShellApp.run(...)`

The API uses ordinary Java classes and fluent methods instead of C# extension
method patterns. The stable interoperability boundary remains the
ResourceTemplate JSON shape.

`withReference(...)` and `dependsOn(...)` intentionally model different
CloudShell relationships. References feed provider-specific service discovery
bindings. Dependencies feed lifecycle ordering and are honored by commands such
as `resource action execute <resource> start --start-dependencies`.

The lifecycle methods match the shared launcher vocabulary:

- `toJson()` emits the ResourceTemplate.
- `apply(...)` applies the template to an already-running Control Plane.
- `start(...)` uses the CLI's daemon-style `template apply --start` path.
- `run(...)` starts the host in the foreground, applies the template after the
  Control Plane is ready, and keeps the host tied to the Java launcher
  process lifetime.

Configuration Store and Secrets Vault seed values use declarative seed
builders:

```java
ConfigurationStoreResource settings = app.addConfigurationStore("settings")
    .withSeed(seed -> seed.setting("Sample--Message", "Hello from Java"));

SecretsVaultResource secrets = app.addSecretsVault("secrets")
    .withSeed(seed -> seed.secret("Sample--ApiKey", "local-development-secret"));

app.addJavaApp("api", "src/main/java", "target/api.jar")
    .withEnvironmentVariable("Sample__Message", settings.setting("Sample--Message"))
    .withEnvironmentVariable("Sample__ApiKey", secrets.secret("Sample--ApiKey"));
```

```java
CloudShellLauncherOptions options = new CloudShellLauncherOptions()
    .withCliProject(Path.of("CloudShell.Cli/CloudShell.Cli.csproj"))
    .withHostProject(Path.of("CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj"))
    .withHostUrl("http://127.0.0.1:5100")
    .withDataDirectory(Path.of(".cloudshell"));

app.run(options);
```

Compile the package sources directly:

```bash
javac -d target/classes $(find src/main/java -name '*.java')
```

Run the package tests:

```bash
./test.sh
```
