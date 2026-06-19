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
  `WithContainerHost(...)`.
- On-premise mode: an orchestrator such as Docker Compose owns lifecycle,
  networking, and exposure for the resource graph.

The sample also shows a lightweight SQL Server service resource with a
resource-owned local endpoint and a Local Storage-backed data volume:

```csharp
var localStorage = resources
    .AddLocalStorage("local")
    .WithDisplayName("Local Storage")
    .UseLocation("./Data/storage");

var sqlData = resources
    .AddVolume("sql-data")
    .WithDisplayName("SQL Server Data")
    .UseStorage(localStorage, "sql-server");

resources
    .AddSqlServer("sql-server", dataVolume: sqlData);
```

`AddSqlServer(...)` is provided by the application provider. It projects an
`application.sql-server` service resource, declares a `tds` endpoint on the
resource itself, and optionally mounts a data volume at `/var/opt/mssql`. The
local implementation is still container-backed, but the SQL Server resource is
not exposed as a generic container app and does not use the container app image
deployment or replica surface.

The storage graph is intentionally explicit. The Local Storage resource is a
`Storage` class resource that announces the `FileSystem` medium. The SQL data
volume is a sub-item of that storage resource and projects the same medium.
The default local Docker runner materializes that `FileSystem` volume as a bind
mount when SQL Server starts.
