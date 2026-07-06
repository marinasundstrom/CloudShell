# JavaScript Applications

Use the JavaScript app resource type for local JavaScript and Node.js
applications that should participate in the CloudShell local development
resource graph. These resources project as `application.javascript-app`.

The first JavaScript app provider assumes the app runs on Node.js. It records
that assumption as `javascript.engine: node` and uses package-manager/script
attributes for the development command. Future extensions can add more focused
registration helpers for frameworks such as Vite, Next.js, or workers without
changing the base resource type.

For shared application-provider behavior, see
[Application resources](application-resources.md). For related resource types,
see [ASP.NET Core applications](aspnet-core-applications.md),
[Executable applications](executable-applications.md), and
[Container apps](container-apps.md).

## Declaration

Programmatic declarations use `AddJavaScriptApp(...)` with a scoped resource
name and project path. The provider derives the canonical resource ID from the
name:

```csharp
resources
    .AddJavaScriptApp("frontend", "src/frontend")
    .WithDisplayName("Frontend")
    .WithPackageManager("pnpm")
    .WithScript("dev")
    .WithHttpEndpoint(port: 5173, targetPort: 5173, host: "localhost");
```

The default declaration assumes:

```yaml
javascript:
  engine: node
  packageManager: npm
  script: dev
```

JavaScript app resources can declare endpoint requests, environment variables,
service references, health checks, log sources, and volume mounts using the
same Resource model patterns as other application resources. The default local
runtime starts the declared package-manager script from the project directory,
tracks process state, and exposes process logs and metrics through Resource
Manager.

Use `AsContainerApp(...)` when a JavaScript app should be authored as a JavaScript
project but run as a scalable container app:

```csharp
resources
    .AddJavaScriptApp("frontend", "src/frontend")
    .AsContainerApp(tag: "dev", dockerfile: "Dockerfile")
    .WithReplicas(3)
    .WithHttpEndpoint(port: 5173, targetPort: 8080);
```

The projection changes the Resource Manager resource to
`application.container-app` while retaining JavaScript project metadata such as
`project.path`, `javascript.engine`, `javascript.packageManager`, and
`javascript.script`. That lets the same authored app participate in container
app deployment, replica, monitoring, and runtime views. For JavaScript, the
current packaging path expects a container build context and normally a
project-owned Dockerfile. Docker is the first local runtime target; the
resource model stores container intent so other OCI-compatible targets such as
Podman can be added behind the container host boundary.

When a JavaScript app references Configuration Store or Secrets Vault
resources, the provider derives `CLOUDSHELL_CONFIGURATION_*` and
`CLOUDSHELL_SECRETS_*` binding variables for the running process. The same
resolver is used for JavaScript apps projected as `application.container-app`.
Node.js code consumes those bindings through the TypeScript runtime SDK under
`sdk/typescript/configuration-client`.

Runtime client packages stay separate from the resource type: the resource type
describes how the app participates in the graph, while client packages make it
easier for the running app to consume CloudShell-managed services.

## Frontend Applications And TypeScript

The base JavaScript app type should remain framework-neutral. Frontend
frameworks and build systems such as Vite, Next.js, Remix, Angular, Vue, React
tooling, or other bundlers can require different development commands, dev
server endpoint behavior, environment-variable conventions, build output
locations, and hot reload semantics. Future provider extensions can add
framework-specific helpers that still compile to `application.javascript-app`
or to narrower resource types when a framework needs distinct runtime behavior.

TypeScript should be treated as part of the JavaScript app story, but not as a
single runtime mode. Plain Node.js server applications may run TypeScript files
directly when the selected Node.js version and project configuration support
that path. Browser-focused frontend applications usually need a framework or
build engine to compile, bundle, transform, and serve the application during
development. CloudShell should model those tools as provider-owned runtime
adapter concerns rather than adding bundler-specific concepts to the core
resource definition.

Hot reload is provider-owned behavior. The current local runtime starts the
selected development command and lets the framework's own watcher or HMR server
reload the app while CloudShell continues to manage resource identity,
references, endpoints, logs, metrics, and operations.

## Sample

The `samples/JavaScriptApp` sample declares:

- an `application.javascript-app` frontend rooted at `samples/JavaScriptApp/App`
- a Configuration Store resource referenced by the JavaScript app
- environment variables that show how the app can receive another resource's
  endpoint during local development

Run the app host in a foreground terminal. The host declares the JavaScript app
resource and starts the Control Plane and Web UI in the same process:

```bash
samples/JavaScriptApp/cloudshell.sh run-no-auth
```

From a second terminal, open the Web UI, list resources, and start the
JavaScript app:

```bash
samples/JavaScriptApp/cloudshell.sh open
samples/JavaScriptApp/cloudshell.sh resources
samples/JavaScriptApp/cloudshell.sh start-app
```

The helper still exposes daemon commands for CLI daemon testing. Daemon mode
is useful for automation, but the normal local-development sample flow keeps
the Control Plane bound to the foreground host process.

The Node app itself can also be run directly while the resource type remains
the CloudShell graph and Resource Manager representation:

```bash
cd samples/JavaScriptApp/App
npm run dev
```

The `samples/TypeScriptContainerApp` sample covers the launcher-authored
container wrapping use case. It uses the TypeScript launcher to declare a
JavaScript app, project it as an `application.container-app`, build the image
from `App/Dockerfile`, and declare two replicas. It also declares
Configuration Store, Secrets Vault, resource identity, and read grants; the
container app reads both services through
`sdk/typescript/configuration-client` from `/configuration`.

The `samples/JavaScriptContainerApp` sample keeps the same runtime SDK proof in
a C# host-composition sample so provider registration and host-owned behavior
remain covered separately from launcher authoring.

The `samples/ReactTypeScriptApp` sample covers a browser frontend with a
backend. A TypeScript launcher declares a React/Vite frontend, a Node backend
API, a Configuration Store dependency consumed by the backend, and a
load-balancer resource that routes frontend and `/api` traffic to the
appropriate application resources.
