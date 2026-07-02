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

## Java Staging

The first Java launcher builder lives in `samples/JavaAppHost` as
sample-local prototype code. Keep it there until the Java authoring shape is
proven across more scenarios. A future Java app-host authoring package should
own ResourceTemplate builders and CLI apply/start integration.

The Java runtime service clients under `sdk/java/cloudshell` are a separate
surface. They are for Java applications that are already running and need to
consume CloudShell-managed services through injected environment bindings.
