# CloudShell Container Host Sample

This sample shows the minimal local development flow for container-backed
resources.

```csharp
var cloudShell = builder
    .AddCloudShell()
    .AddApplicationProvider()
    .UseLocalDevelopmentDefaults();
```

- `UseLocalDevelopmentDefaults()` registers Docker as the default container
  host and selects the built-in default orchestrator when Resource Manager
  settings have not already been changed.

CloudShell has two usage modes:

- Local dev orchestrator: the default orchestrator runs resources locally.
  Container resources use the default Docker host unless a resource calls
  `WithContainerEngine(...)`.
- On-premise mode: an orchestrator such as Docker Compose owns lifecycle,
  networking, and exposure for the resource graph.

The sample also shows a provider-style binding with a resource-owned local endpoint:

```csharp
resources
    .AddSqlServer("sql-server")
    .WithImage("mcr.microsoft.com/mssql/server:2022-latest");
```

`AddSqlServer(...)` is implemented locally by composing the core `AddContainer(...)` method, declaring a `tds` endpoint on the resource itself, and returning `IContainerResourceBuilder`, so callers can override the image without knowing which container engine will run it. Service resources are optional in local development; resource-owned endpoints are enough for direct access and service discovery.
