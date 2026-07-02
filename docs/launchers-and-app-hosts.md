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

Language launcher packages live under the repository's top-level `Launchers/`
folder:

- `Launchers/CSharp/CloudShell.AppHost.Launcher`
- `Launchers/TypeScript/cloudshell`
- `Launchers/Java/cloudshell-launcher`

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

Launcher packages and samples should use the same lifecycle vocabulary across
languages:

- `template` or `toJson`: emit the ResourceTemplate without applying it.
- `apply`: apply the template to an already-running Control Plane.
- `start`: start or reuse a daemon-style local host, then apply the template.
- `run`: start the host in the foreground, apply the template, and keep the
  host process tied to the launcher command lifetime.

The API shape can be idiomatic to each language. C# can use records and async
methods, TypeScript can use object-literal options and promises, and Java can
use ordinary option classes and fluent methods. The behavior behind those
verbs should remain consistent.

Resource relationships should keep the same meaning in every launcher.
Service-discovery references and startup dependencies are separate concepts:
references feed provider-specific binding projection, while dependencies feed
lifecycle ordering and `--start-dependencies` behavior.

The experimental TypeScript launcher package under `Launchers/TypeScript/cloudshell`
proves the non-C# authoring shape. It has hand-authored builders for the first
resource types, emits ResourceTemplate JSON, and can call the CLI apply path.
`samples/TypeScriptAppHost` exercises the end-to-end flow with
`CloudShell.LocalDevelopmentHost`.

The experimental Java launcher package under `Launchers/Java/cloudshell-launcher`
contains Java-native ResourceTemplate builders for Java launcher apps.
`samples/JavaAppHost` consumes that package from a small Java source-file
launcher, emits a ResourceTemplate, applies it through the CLI, and can run the
local host in the foreground for launcher-owned lifetime scenarios.

## Java Staging

The Java launcher builder is experimental. It owns the initial Java
ResourceTemplate authoring shape and the first Java process helpers for
`apply`, daemon-backed `start`, and foreground `run`.

The Java runtime service clients under `sdk/java/cloudshell` are a separate
surface. They are for Java applications that are already running and need to
consume CloudShell-managed services through injected environment bindings.
