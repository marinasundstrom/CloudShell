---
title: Go apps
description: Run a local Go project or binary as an endpoint-aware and observable CloudShell resource.
---

# Go apps

A Go app resource represents a Go service or program independently from the process that happens to run it. The same resource identity connects lifecycle, endpoints, dependencies, health, logs, and telemetry.

> [!NOTE]
> Go app resources and the Go launcher are preview functionality intended for trusted local development hosts.

## What you get

- **Source or binary execution.** Run a package with the Go toolchain or point CloudShell at a prebuilt binary.
- **Service endpoints.** Publish an HTTP or TCP endpoint for people and dependent resources.
- **Dependency context.** Connect the app to configuration, secrets, databases, messaging, or other services.
- **Consistent operations.** Use the same Resource Manager lifecycle and diagnostic views as other application languages.

## Minimal resource template

```yaml
resources:
  - type: application.go-app
    name: go-api
    project:
      path: ./app
    command: go
    packagePath: .
    endpoints:
      - name: http
        protocol: http
        targetPort: 8080
        port: 8080
        exposure: Local
```

Use `binaryPath` when CloudShell should run a prebuilt program instead of `go run`.

## When to choose it

Choose a Go app while iterating on local source. Choose a [container app](container-apps.md) when validating the compiled artifact inside an image or targeting a shared host.
