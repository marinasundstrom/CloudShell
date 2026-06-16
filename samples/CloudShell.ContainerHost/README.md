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

The sample also shows a provider-style SQL Server binding with a resource-owned
local endpoint and a Local Storage-backed data volume:

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

`AddSqlServer(...)` is implemented locally by composing the core
`AddContainer(...)` method, declaring a `tds` endpoint on the resource itself,
optionally mounting a data volume at `/var/opt/mssql`, and returning
`IContainerResourceBuilder`. This is a sample shortcut, not the future managed
SQL Server service API. A provider-owned SQL Server builder should expose
validated SQL Server settings such as version and edition rather than arbitrary
container image override. Service resources are optional in local development;
resource-owned endpoints are enough for direct access and service discovery.

The storage graph is intentionally explicit. The Local Storage resource is a
`Storage` class resource that announces the `FileSystem` medium. The SQL data
volume is a sub-item of that storage resource and projects the same medium.
The default local Docker runner materializes that `FileSystem` volume as a bind
mount when SQL Server starts.
