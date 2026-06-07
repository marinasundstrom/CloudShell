# CloudShell Container Host Sample

This sample shows the local development flow for container-backed resources and the split between container engine registration and orchestration registration.

```csharp
var cloudShell = builder
    .AddCloudShell()
    .AddApplicationProvider()
    .UseDocker()
    .AddDockerComposeOrchestrator();
```

- `UseDocker()` registers Docker as the container engine.
- `AddDockerComposeOrchestrator()` makes Docker Compose available as an orchestrator.
- If no orchestrator has been selected in Resource Manager settings, the default orchestrator remains active.

CloudShell has two usage modes:

- Local dev orchestrator: the default orchestrator runs resources locally. Container resources require a registered default container engine, such as `UseDocker()`, unless a resource calls `WithContainerEngine(...)`.
- On-premise mode: an orchestrator such as Docker Compose owns lifecycle, networking, and exposure for the resource graph.

The sample also shows a provider-style binding with a resource-owned local endpoint:

```csharp
resources
    .AddSqlServer("sql-server")
    .WithImage("mcr.microsoft.com/mssql/server:2022-latest");
```

`AddSqlServer(...)` is implemented locally by composing the core `AddContainer(...)` method, declaring a `tds` endpoint on the resource itself, and returning `IContainerResourceBuilder`, so callers can override the image without knowing which container engine will run it. Service resources are optional in local development; resource-owned endpoints are enough for direct access and service discovery.
