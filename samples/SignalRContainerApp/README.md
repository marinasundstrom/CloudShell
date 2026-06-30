# SignalR Container App sample

This sample models a Blazor WebAssembly frontend talking to a replicated
SignalR backend through a CloudShell container app.

The declared CloudShell resources are intentionally small:

- `application.aspnet-core-project:signalr-frontend` hosts the Blazor
  WebAssembly app.
- `application.container-app:signalr-api` runs the SignalR backend with three
  replica slots and cookie session affinity.

The backend replicas and sticky ingress are runtime materialization for the
container app. They are not authored as user resources. Until the shared
container app runtime/orchestrator exists, the sample uses a local bridge that
starts replica processes and exposes the declared API endpoint.

The local bridge keeps SignalR negotiate and WebSocket requests on the same
replica by tracking negotiated connection tokens. This is sample-local proof
for sticky real-time routing; the shared container app runtime should own the
general behavior later.

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
- SignalR API replicas: `http://localhost:5013` through `http://localhost:5015`
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

The local bridge is intentionally temporary. The next runtime slice should move
this behavior behind the shared container app runtime/orchestrator path.
