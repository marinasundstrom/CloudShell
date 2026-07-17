# SignalR Container App sample

This sample models a Blazor WebAssembly frontend talking to a replicated
SignalR backend through a CloudShell container app.

The declared CloudShell resources are intentionally small:

- `application.dotnet-app:signalr-frontend` hosts the Blazor
  WebAssembly app.
- `application.container-app:signalr-api` runs the SignalR backend with three
  replica slots and cookie session affinity.

The backend replicas and sticky ingress are runtime materialization for the
container app. They are not authored as user resources. The sample registers
the built-in local Docker container app runtime once; the runtime builds the
project-backed API image from the resource's `project.path`, starts three
replica containers, and exposes the declared API endpoint through a local
Traefik ingress container.
This local Docker/Traefik execution path is an accepted local-development MVP
bridge. The sample proves the SignalR workload, sticky-routing intent, and
container-app runtime boundary without making replica containers user-authored
resources or committing to the final distributed-agent implementation.

The browser frontend uses the default SignalR negotiation flow through a
same-origin frontend proxy path. That proxy forwards HTTP and WebSocket
traffic to the backend container app ingress. The app declares cookie session
affinity at the container app resource level with a five-minute lifetime, and
the local Docker runtime projects that intent into Traefik sticky routing.
Normal HTTP requests that carry the affinity cookie are routed back to the
selected replica too; SignalR uses the same behavior so negotiate, reconnect,
fallback transport, and WebSocket upgrade requests stay on the same replica.
After the WebSocket is established, the long-lived connection remains on that
replica without needing a long-lived browser affinity cookie.
The sample enables affinity because the replicated backend demonstrates
real-time, replica-local connection continuity. It keeps the affinity window
short so a later site visit can be assigned to a fresh replica. Stateless
container apps should normally leave affinity disabled.

## Run

```bash
dotnet run --project samples/SignalRContainerApp/CloudShell.SignalRContainerApp.csproj
```

Open Resource Manager and inspect the frontend and backend resources. Starting
the backend container app starts three local API replica processes and a
sticky local ingress endpoint. Starting the frontend exposes the Blazor
WebAssembly app.

When the CloudShell host URL is supplied with `--urls`, sample resource ports
are derived from that port. For example:

```bash
dotnet run --project samples/SignalRContainerApp/CloudShell.SignalRContainerApp.csproj -- --urls http://localhost:5011
```

uses these defaults:

- CloudShell host: `http://localhost:5011`
- SignalR API ingress: `http://localhost:5012`
- Blazor frontend: `http://localhost:5016`

Without `--urls`, the sample falls back to:

- SignalR API ingress: `http://localhost:5095`
- Blazor frontend: `http://localhost:5096`

Override ports with:

```bash
SignalRContainerApp__ApiPort=6095 \
SignalRContainerApp__ReplicaPortStart=6097 \
SignalRContainerApp__FrontendEndpoint=http://localhost:6096 \
dotnet run --project samples/SignalRContainerApp/CloudShell.SignalRContainerApp.csproj
```

Implicit local Docker runtime container names are scoped to the running
CloudShell host instance. That lets this sample, another local CloudShell host,
and sample smoke tests use the same resource IDs without reusing each other's
Docker containers or ingress.
