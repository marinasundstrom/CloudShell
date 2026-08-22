---
title: JavaScript apps
description: Run Node.js, JavaScript, TypeScript, or Bun projects as observable CloudShell application resources.
---

# JavaScript apps

A JavaScript app resource runs a local Node.js, JavaScript, TypeScript, or Bun project while CloudShell supplies resource identity, lifecycle, endpoints, relationships, health, and diagnostics.

> [!NOTE]
> JavaScript app resources are in preview. Local project paths and package-manager commands should only be accepted by a trusted development host.

## What you get

- **Package-aware startup.** Select Node.js or Bun, a package manager, and the script CloudShell should run.
- **Endpoints and relationships.** Publish frontend or API endpoints and reference the services the app consumes.
- **Process visibility.** Inspect state, logs, activity, health, and monitoring from the same resource page.
- **A path to containers.** Keep the JavaScript project model while projecting a packaged workload as a container app later.

## Minimal resource template

```yaml
resources:
  - type: application.javascript-app
    name: frontend
    project:
      path: ./web
    runtime: node
    packageManager: npm
    script: dev
    endpoints:
      - name: http
        protocol: http
        targetPort: 5173
        port: 5173
        exposure: Local
```

The project directory should contain the package manifest and the selected script. See [Build your first CloudShell app](../tutorials/first-app.md) for a complete JavaScript example.

## When to choose it

Choose a JavaScript app for the shortest edit-and-run loop against trusted local source. Choose a [container app](container-apps.md) when testing the packaged image or running on a shared CloudShell host.
