---
title: Get started with the CloudShell CLI
description: Install the CloudShell preview CLI, describe a local application environment in YAML, and open Resource Manager.
---

# Get started with the CloudShell CLI

CloudShell is currently available as a **preview**. The supported first-run path is the `cloudshell` .NET tool with a project-local `cloudshell.yaml` file. Expect packages, commands, and resource shapes to evolve while the project is in preview.

## Prerequisites

- A current .NET SDK compatible with the preview package
- Docker for container-backed resources and built-in services
- A local application you want to describe and run

## 1. Install the preview CLI

Preview packages are published to the CloudShell MyGet feed. Choose an explicit version from the [CloudShell preview feed](https://www.myget.org/gallery/cloudshell):

```bash
dotnet tool install --global CloudShell.Cli \
  --add-source https://www.myget.org/F/cloudshell/api/v3/index.json \
  --version <preview-version>
```

CloudShell uses explicit preview versions so an early project does not update unexpectedly.

## 2. Add `cloudshell.yaml`

Create a resource template beside your application. This small example runs an ASP.NET Core project and gives it a configuration service:

```yaml
name: hello-cloudshell
environment: local

resources:
  - type: configuration.store
    name: app-settings
    displayName: Application settings
    endpoint: http://localhost:5266

  - type: application.dotnet-app
    name: api
    displayName: Sample API
    dependsOn:
      - resourceId: configuration.store:app-settings
    project:
      path: ./Api/Api.csproj
    endpoints:
      - name: http
        protocol: http
        host: localhost
        port: 5265
        targetPort: 5265
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

- Learn the [core concepts](concepts.md).
- Browse the [resource catalog](resources/index.md).
- See how [telemetry and observability](observability.md) fit together.
- Try the complete [`samples/YamlAppHost`](https://github.com/marinasundstrom/CloudShell/tree/main/samples/YamlAppHost) example.
