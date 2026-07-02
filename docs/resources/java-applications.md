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
through the Java SDK clients under `sdk/java/cloudshell`. A service-discovery
reference is not a startup dependency by itself. Launcher code should also
declare `dependsOn(...)` when the referenced service should be started before
the Java app through dependency startup.

## Launcher Shape

CloudShell's cross-language boundary is the ResourceTemplate, not C# builder
syntax. Java launchers should expose normal Java classes and fluent methods
while still emitting the same resource type IDs, attributes, references,
endpoint requests, and metadata used by C# and TypeScript launchers.
See [Launchers and app hosts](../launchers-and-app-hosts.md) for the shared
terminology and package-boundary guidance.

`samples/JavaAppHost` demonstrates that shape with a Java source-file launcher.
Its small entrypoint consumes the experimental Java launcher package under
`Launchers/Java/cloudshell-launcher`. That package owns Java ResourceTemplate
authoring and stays separate from the Java runtime service-client SDK, which
is for Java applications that are already running and need to consume
CloudShell-managed services. The Java launcher package supports template
emission, apply, daemon-backed start, and foreground run so Java follows the
same launcher lifecycle vocabulary as the C# and TypeScript integrations.
Its builders expose both `withReference(...)` for service discovery and
`dependsOn(...)` for lifecycle ordering.

## Samples

`samples/JavaApp` declares a Java HTTP app, Configuration Store, and Secrets
Vault in a combined CloudShell host. The Java app compiles against
`sdk/java/cloudshell` and reads configuration/secrets through
environment-discovered clients.

`samples/JavaAppHost` demonstrates a Java launcher source file that emits a
ResourceTemplate and applies it through the CloudShell CLI/local-development
host profile.
