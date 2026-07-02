# Launchers And App Hosts

CloudShell uses **host profile** for the process that composes CloudShell
itself: Control Plane, Web UI, provider packages, runtime adapters,
authentication, persistence, and environment-level services. The built-in local
development host profile is `CloudShell.LocalDevelopmentHost`.

CloudShell uses **launcher** or **app host launcher** for a language-specific
authoring program that defines a resource graph, emits a `ResourceTemplate`,
and asks the CloudShell CLI or Control Plane API to apply that template to a
host profile. A launcher is not the Control Plane and should not reference
Control Plane stores, provider runtimes, or UI hosting packages.

This naming keeps three responsibilities separate:

- **Host profile**: runs CloudShell and installs providers/runtime adapters.
- **Launcher**: authors and applies a resource graph for a target environment.
- **Runtime service client**: runs inside an application workload and consumes
  CloudShell-managed services such as Configuration Store or Secrets Vault.

## Cross-Language Boundary

The stable cross-language boundary is the CloudShell Resource model:

- `ResourceTemplate`
- resource type IDs
- provider IDs
- resource IDs and references
- endpoint requests
- non-secret attributes
- metadata
- CLI apply/start behavior

Language SDKs should feel natural in their own ecosystem while preserving that
same model. C# can use extension methods over `ResourceGraphBuilder`.
TypeScript can use builder objects and object-literal options. Java should use
normal classes, fluent methods, records where useful, and package boundaries
that feel ordinary to Java developers. A Java API should not copy C# extension
method patterns directly.

## Current Implementations

`CloudShell.AppHost.Launcher` is the current C# launcher/app-host authoring
package. It reuses Resource Model builders, emits a `ResourceTemplate`, and
delegates local host startup or template apply to the CloudShell CLI. It does
not reference Control Plane stores, provider runtimes, or UI hosting packages.

`CloudShell.LocalDevelopmentHost` is the stable built-in local host profile for
launchers that do not need a custom CloudShell host process. It composes the
Control Plane, Web UI, built-in resource model providers, Resource Manager UI
integrations, local runtime adapters, and sample-friendly local persistence
settings. Custom host profiles are reserved for cases where the CloudShell host
process itself needs extra extensions, authentication, persistence, or
host-specific services.

The CloudShell CLI is the shared automation boundary for launcher workflows.
Launchers can use `template apply --start` to start or reuse a local host,
apply a generated template, pass `--data-dir` for launcher-local CloudShell
data, and later target an existing Control Plane by URL. See
[CloudShell CLI](cli.md) for command details.

The experimental TypeScript hosting package under `sdk/typescript/cloudshell`
proves the non-C# authoring shape. It has hand-authored builders for the first
resource types, emits ResourceTemplate JSON, and can call the CLI apply path.
`samples/TypeScriptAppHost` exercises the end-to-end flow with
`CloudShell.LocalDevelopmentHost`.

Java app-host authoring is still sample-local. `samples/JavaAppHost` contains
a Java source-file launcher that emits a ResourceTemplate and applies it
through the CLI. Keep Java authoring builders there until CloudShell is ready
for a dedicated Java app-host authoring package.

## Java Staging

The first Java launcher builder lives in `samples/JavaAppHost` as
sample-local prototype code. Keep it there until the Java authoring shape is
proven across more scenarios. A future Java app-host authoring package should
own ResourceTemplate builders and CLI apply/start integration.

The Java runtime service clients under `sdk/java/cloudshell` are a separate
surface. They are for Java applications that are already running and need to
consume CloudShell-managed services through injected environment bindings.
