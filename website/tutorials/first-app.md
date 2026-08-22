---
title: Build your first CloudShell app
description: Create a small JavaScript service, describe it in a resource template, and operate it through CloudShell Resource Manager.
---

# Build your first CloudShell app

This tutorial creates a small JavaScript HTTP service and describes it with a CloudShell resource template. The application is ordinary Node.js code; CloudShell adds identity, lifecycle, endpoints, and operational context around it.

CloudShell is in preview. Use a disposable local project and expect CLI and template details to evolve.

## Before you begin

Complete the [CLI installation](../get-started.md#1-install-the-preview-cli). You also need Node.js and npm available on your local machine.

## 1. Create the application

Create a new folder with an `app` subfolder. Add `app/package.json`:

```json
{
  "name": "hello-cloudshell",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "node server.js"
  }
}
```

Add `app/server.js`:

```javascript
import { createServer } from "node:http";

const port = Number(process.env.PORT ?? 5173);

createServer((request, response) => {
  response.writeHead(200, { "content-type": "application/json" });
  response.end(JSON.stringify({
    message: "Hello from CloudShell",
    path: request.url
  }));
}).listen(port, "127.0.0.1", () => {
  console.log(`JavaScript service listening on http://127.0.0.1:${port}`);
});
```

## 2. Define the resource

Create `cloudshell.yaml` in the parent folder:

```yaml
name: hello-cloudshell
environment: local

resources:
  - type: application.javascript-app
    name: hello-api
    displayName: Hello API
    project:
      path: ./app
      environmentVariables:
        PORT:
          value: "5173"
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

The template declares one JavaScript application resource. The project path is relative to `cloudshell.yaml`; the named endpoint tells CloudShell how the running service can be reached.

## 3. Start CloudShell

Run this from the folder containing the template:

```bash
cloudshell run
```

The CLI starts the local CloudShell host, applies the resource template, and prints the Resource Manager URL. Keep this terminal open.

## 4. Start and inspect the app

Open Resource Manager, select **Hello API**, and choose **Start**. When its state becomes **Running**:

1. Open the `http` endpoint.
2. Return to the resource and inspect its current state and relationships.
3. Open **Logs** to see the message printed by the Node.js process.
4. Open **Activity** to see the lifecycle operation.

The application remains normal JavaScript. CloudShell owns the resource context around the process.

## 5. Add another resource

Add this entry before the application in `cloudshell.yaml`:

```yaml
  - type: configuration.store
    name: settings
    displayName: Application settings
```

Then add a dependency to `hello-api`:

```yaml
    dependsOn:
      - resourceId: configuration.store:settings
```

Restart `cloudshell run` to apply the updated graph. Resource Manager now shows the relationship between the application and its configuration service. A production application would use an appropriate runtime client and an access grant before reading protected values.

## Where to go next

- Learn how [resource templates](../resource-templates.md) represent desired state.
- Compare [development and shared hosting](../development-and-hosting.md).
- Explore all [application resource types](../resources/applications.md).
- Read about [launchers](../launchers.md) when you are ready for code-based graph authoring.
