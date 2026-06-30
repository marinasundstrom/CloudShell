# SignalR Container App sample

This sample models a Blazor WebAssembly frontend talking to a replicated
SignalR backend through a CloudShell container app.

The declared CloudShell resources are intentionally small:

- `application.aspnet-core-project:signalr-frontend` hosts the Blazor
  WebAssembly app.
- `application.container-app:signalr-api` runs the SignalR backend with three
  replica slots and cookie session affinity.

The backend replicas and Traefik ingress container are runtime materialization
for the container app. They are not authored as user resources.

This sample currently declares the intended resources only. Starting the
container app backend waits for the shared container app runtime/orchestrator
work so samples do not need local bridge classes.

## Run

```bash
dotnet run --project samples/SignalRContainerApp/CloudShell.SignalRContainerApp.csproj
```

Open Resource Manager and inspect the frontend and backend resources. Once the
shared container app runtime is available, starting the frontend will expose
the Blazor WebAssembly app and starting the backend container app will
materialize the replicated SignalR service.

The sample defaults are:

- CloudShell host: `http://localhost:5011`
- SignalR API ingress: `http://localhost:5095`
- Blazor frontend: `http://localhost:5096`

Override ports with:

```bash
SignalRContainerApp__ApiPort=6095 \
SignalRContainerApp__FrontendEndpoint=http://localhost:6096 \
dotnet run --project samples/SignalRContainerApp/CloudShell.SignalRContainerApp.csproj
```

Docker-backed runtime materialization is intentionally deferred to the shared
container app runtime/orchestrator path.
