---
title: Application resources
description: Model local projects, executables, and containerized workloads as CloudShell application resources.
---

# Application resources

Application resources represent runnable workloads. They can expose endpoints, dependencies, health, lifecycle actions, logs, telemetry, and monitoring through the same Resource Manager experience.

| Resource | Type ID | Use it for |
| --- | --- | --- |
| Executable application | `application.executable` | A host-local command, worker, tool, emulator, or script. |
| .NET application | `application.dotnet-app` | A local .NET project with project-aware startup and endpoint discovery. |
| JavaScript application | `application.javascript-app` | A Node.js or package-manager-backed JavaScript/TypeScript project. |
| Java application | `application.java-app` | A local JVM project or application process. |
| Go application | `application.go-app` | A local Go service or program. |
| Python application | `application.python-app` | A local Python module, script, or service. |
| Container app | `application.container-app` | A managed containerized workload with a clearer isolation and placement boundary. |

## Which one should I choose?

Use a language-specific resource for quick local iteration against source on the same trusted machine as the CloudShell host. Use a **Container app** when you want to validate the container boundary or run on a shared/self-hosted environment. Host-local project paths are privileged inputs and should not be exposed to ordinary users of a remote host.

Container apps are the preferred application deployment shape for team-owned environments. A provider may create replica or low-level container resources underneath the application; the container app remains the stable resource you operate.

## Featured resource guides

- [Executable apps](executable-apps.md) — host-local commands, tools, workers, emulators, and scripts.
- [.NET apps](dotnet-apps.md) — project-aware local development with distributed tracing.
- [JavaScript apps](javascript-apps.md) — Node.js, TypeScript, JavaScript, and Bun projects.
- [Java apps](java-apps.md) — JARs, main classes, JVM arguments, and service relationships.
- [Go apps](go-apps.md) — local Go packages or prebuilt binaries.
- [Python apps](python-apps.md) — Python scripts, modules, APIs, and workers.
- [Container apps](container-apps.md) — a stable application boundary over replicas and runtime-managed containers.

## Common capabilities

Capabilities depend on the resource type, provider, and current state, but application resources can provide:

- start, stop, and restart actions;
- HTTP, TCP, and provider-specific endpoints;
- service-discovery names and dependencies;
- configuration, secret, identity, and storage relationships;
- readiness and health checks; and
- logs, traces, metrics, monitoring, and activity.
