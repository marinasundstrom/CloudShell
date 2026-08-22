---
title: Get started with the CloudShell CLI
description: Install the CloudShell preview CLI, describe a local application environment in YAML, and open Resource Manager.
---

# Get started with the CloudShell CLI

CloudShell is currently available as a **preview**. The supported first-run path is the `cloudshell` .NET tool with a project-local `cloudshell.yaml` file. Expect packages, commands, and resource shapes to evolve while the project is in preview.

## Prerequisites

- A current .NET SDK compatible with the preview package
- Docker for container-backed resources and built-in services
- A local application and its normal language runtime; the example below uses Node.js and npm

## 1. Install the preview CLI

Preview packages are published to the CloudShell MyGet feed. Choose an explicit version from the [CloudShell preview feed](https://www.myget.org/gallery/cloudshell):

```bash
dotnet tool install --global CloudShell.Cli \
  --add-source https://www.myget.org/F/cloudshell/api/v3/index.json \
  --version <preview-version>
```

CloudShell uses explicit preview versions so an early project does not update unexpectedly.

## 2. Add `cloudshell.yaml`

Create a resource template beside your application. This small example runs a JavaScript service; CloudShell uses the same template model for Java, Go, Python, .NET, executable, and container workloads:

```yaml
name: hello-cloudshell
environment: local

resources:
  - type: application.javascript-app
    name: api
    displayName: Sample API
    project:
      path: ./app
    runtime: node
    packageManager: npm
    script: dev
    endpoints:
      - name: http
        protocol: http
        host: localhost
        port: 5173
        targetPort: 5173
        exposure: Local
```

Paths are relative to the folder containing the YAML file. Keep secrets out of source-controlled templates; use a Secrets Vault resource for secret values.

## 3. Run the environment

From the folder containing `cloudshell.yaml`, run:

```bash
cloudshell run
```

The CLI starts the bundled local development host, applies the resource template, and prints the Resource Manager URL. The host stays attached to the terminal; press <kbd>Ctrl</kbd>+<kbd>C</kbd> to stop it.

## 4. Operate it in Resource Manager

Open the printed URL to:

- check resource state and health;
- start or stop supported resources;
- open application and management endpoints;
- follow dependencies in the resource graph; and
- inspect logs, traces, metrics, monitoring, and activity when the resource exposes those signals.

## Next steps

- Follow [Build your first CloudShell app](tutorials/first-app.md) for a complete JavaScript walkthrough.
- Learn the [core concepts](concepts.md).
- Understand [development and shared hosting](development-and-hosting.md).
- Learn how [resource templates](resource-templates.md) and [launchers](launchers.md) author the same graph.
- Browse the [resource catalog](resources/index.md).
- See how [telemetry and observability](observability.md) fit together.
- Try the complete [`samples/YamlAppHost`](https://github.com/marinasundstrom/CloudShell/tree/main/samples/YamlAppHost) example.
