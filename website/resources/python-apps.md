---
title: Python apps
description: Run a local Python module, script, API, or worker through the common CloudShell resource experience.
---

# Python apps

A Python app resource brings a Python service or worker into the environment graph without making the UI or Control Plane Python-specific. CloudShell operates the resource; the workload remains normal Python code.

> [!NOTE]
> Python app resources and the Python launcher are in preview. Project and script paths are privileged local-host inputs.

## What you get

- **Script or module startup.** Select the Python command and run a script or module with application arguments.
- **Endpoint and dependency wiring.** Connect an API to the resources it consumes and publish its reachable endpoint.
- **Runtime diagnostics.** Inspect lifecycle, logs, health, monitoring, and activity in Resource Manager.
- **CloudShell service access.** Receive referenced service endpoints and workload identity details through the runtime environment.

## Minimal resource template

```yaml
resources:
  - type: application.python-app
    name: python-api
    project:
      path: ./app
    command: python3
    scriptPath: app.py
    endpoints:
      - name: http
        protocol: http
        targetPort: 5188
        port: 5188
        exposure: Local
```

Use `module` instead of `scriptPath` when the application should start with Python's module mode.

## When to choose it

Choose a Python app for a fast local source workflow. Choose a [container app](container-apps.md) for a packaged runtime, shared hosting, or replica-based operation.
