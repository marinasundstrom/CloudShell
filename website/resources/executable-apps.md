---
title: Executable apps
description: Run a trusted local command, tool, worker, emulator, or script as an operable CloudShell resource.
---

# Executable apps

An executable app gives a host-local command a stable place in the resource graph. It is useful for workers, tools, emulators, scripts, and services that do not need a language-specific provider.

> [!NOTE]
> Executable apps are preview functionality for trusted hosts. Paths, arguments, and working directories are privileged host inputs and should not be offered to ordinary users of a shared CloudShell environment.

## What you get

- **Command lifecycle.** Start, stop, and restart the process from Resource Manager.
- **Stable resource identity.** Keep endpoints, dependencies, logs, and activity attached to the command across restarts.
- **Process diagnostics.** Capture standard output and error through the resource Logs view.
- **Flexible integration.** Add environment variables, volume mounts, endpoint declarations, and references without writing a custom provider.

## Minimal resource template

```yaml
resources:
  - type: application.executable
    name: background-worker
    path: ./tools/worker
    command:
      arguments: --watch
      workingDirectory: ./tools
```

Paths are resolved on the CloudShell host. Use a plain command name when the executable should be resolved from the host's `PATH`.

## When to choose it

Choose an executable app for a trusted local command that does not benefit from project-aware JavaScript, Java, Go, Python, or .NET behavior. Choose a [container app](container-apps.md) when the workload needs a packaged boundary, replicas, or placement on a shared host.
