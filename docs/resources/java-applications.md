# Java Applications

Use the Java app resource type for local Java and JVM applications that should
participate in the CloudShell local development resource graph. These
resources project as `application.java-app`.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [JavaScript applications](javascript-applications.md),
[ASP.NET Core applications](aspnet-core-applications.md),
[Executable applications](executable-applications.md), and
[Container apps](container-apps.md).

## Declaration

Programmatic C# declarations use `AddJavaApp(...)` with a scoped resource name,
project path, and artifact path:

```csharp
resources
    .AddJavaApp("api", "src/api", "target/app.jar")
    .WithDisplayName("Java API")
    .WithJvmArguments("-Xmx256m")
    .WithHttpEndpoint(port: 5185, targetPort: 5185, host: "localhost");
```

The default local runtime starts `java -jar <artifactPath>`. `java.command`,
`java.jvmArguments`, `java.arguments`, `java.mainClass`, and `java.classPath`
let launchers describe other JVM process shapes without making Maven or Gradle
a CloudShell resource-model concept.

When a Java app references Configuration Store or Secrets Vault resources, the
provider derives `CLOUDSHELL_CONFIGURATION_*` and `CLOUDSHELL_SECRETS_*`
binding variables for the running process. Java code consumes those bindings
through the Java SDK clients under `sdk/java/cloudshell`.

## Launcher Shape

CloudShell's cross-language boundary is the ResourceTemplate, not C# builder
syntax. Java launchers should expose normal Java classes and fluent methods
while still emitting the same resource type IDs, attributes, references,
endpoint requests, and metadata used by C# and TypeScript launchers.
See [Launchers and app hosts](../launchers-and-app-hosts.md) for the shared
terminology and package-boundary guidance.

`samples/JavaAppHost` demonstrates that shape with a Java source-file launcher.
Its small `CloudShellApp`, resource, and endpoint builder classes are
sample-local prototype code. Keep them there until CloudShell has enough Java
launcher experience to publish a separate Java app-host authoring package.
That future package should own ResourceTemplate authoring and CLI apply/start
integration for Java launchers. It should stay separate from the Java runtime
service-client SDK, which is for Java applications that are already running
and need to consume CloudShell-managed services.

## Samples

`samples/JavaApp` declares a Java HTTP app, Configuration Store, and Secrets
Vault in a combined CloudShell host. The Java app compiles against
`sdk/java/cloudshell` and reads configuration/secrets through
environment-discovered clients.

`samples/JavaAppHost` demonstrates a Java launcher source file that emits a
ResourceTemplate and applies it through the CloudShell CLI/local-development
host profile.
