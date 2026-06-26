# CloudShell Application Topology Sample

This sample is the broad MVP application-topology sample. It was forked from
the focused Project Reference sample so the original can remain a small
service-discovery and distributed-tracing baseline while this sample grows into
the full local development scenario.

The host currently declares:

- `Application Topology API` with an auto-assigned HTTP endpoint and a
  development resource identity
- `Application Topology Frontend` on `http://localhost:5218`
- `Application Topology Local Storage`, backed by `./Data/storage`
- `Application Topology SQL Data`, a volume under the local storage resource
- `application-topology-sql-server`, a SQL Server service resource with the
  data volume mounted at `/var/opt/mssql`
- `Application Topology Settings`, a Configuration Store with sample
  application settings injected into the backend API
- `Application Topology Secrets`, a Secrets Vault with a sample secret
  reference injected into the backend API without displaying the value
- `Application Topology Local DNS`, a local-hostname DNS zone with
  `app.application-topology.cloudshell.local` mapped to the frontend HTTP
  endpoint

The frontend uses CloudShell service discovery to call the API:

```csharp
.WithServiceDiscovery()
.WithReference(api)
.DependsOn(api)
```

The host also declares side-by-side graph-backed resources for the API and
frontend through the Resource Definitions bridge. Those graph resources use
`project.references` and `project.serviceDiscoveryName` for frontend-to-API
service discovery, separate configurable graph endpoints
(`ApplicationTopology:GraphApiEndpoint` and
`ApplicationTopology:GraphFrontendEndpoint`), and absolute project paths so the
new ASP.NET Core graph runtime controller can start them without relying on the
old application provider. Current graph smoke coverage starts the graph-backed
Configuration Store and Secrets Vault through the new provider runtime
controllers, verifies authenticated reads from their service APIs, starts the
graph API, verifies that `/settings` is loaded from the graph-backed
Configuration Store and Secrets Vault client integrations, starts the graph
frontend, and exercises frontend-to-API discovery through `/upstream/failure`.
Docker-backed smoke
coverage also starts the graph API and frontend against the existing SQL
Server runtime and verifies the graph frontend `/upstream` path through graph
settings and graph SQL credentials. The host registers a sample-local graph SQL
lifecycle adapter so the graph SQL Server start/stop/restart operations drive
the existing SQL runtime resource for this sample and project cached graph SQL
state after those operations. Docker smoke coverage verifies graph SQL start,
restart, and stop through that adapter, including SQL container creation,
restart recreation, and stop cleanup. It also registers a graph SQL database
ensure-created handler that can materialize the graph database against the
configured SQL Server endpoint without storing administrator credentials in
graph state. The graph API has a declared built-in resource identity, read
grants to the graph Configuration Store and Secrets Vault, and a read/write
grant to the graph SQL Server. The host maps a sample-local graph SQL
credential endpoint so the graph API can exercise `/database` through the same
CloudShell SQL client flow as the old provider path. A reusable graph SQL
credential broker and provider-owned SQL Server runtime implementation remain
provider work.

Both projects use the shared `ServiceDefaults` project for health endpoints,
HTTP client service discovery, JSON console logs, OpenTelemetry tracing, and
basic HTTP request metrics. The host injects CloudShell trace and metric
ingestion settings so `/observability/traces`, `/observability/metrics`, and
each application resource's **Metrics** tab can show spans and request metrics
from both services.

The sample also opts into persisted application log files under
`Observability:ApplicationLogs`. Application logs are stdout/stderr-style
source logs from the application provider; they are separate from resource
event logs. The sample sets `Store` to `File`, writes under
`Data/application-logs`, retains seven days or 5000 entries per log, and leaves
daily file splitting disabled so the current log viewer reads one bounded file
per application log.

The SQL Server resource uses the provider-owned `AddSqlServer(...)` builder.
It is still materialized locally with the SQL Server Linux container image
through `UseLocalDevelopmentDefaults()`, but it projects as an
`application.sql-server` service resource instead of a generic container app.
The resource publishes a local `tds` endpoint on `localhost:14334` by default
and mounts a Local Storage-backed volume so database files can survive
restarts of the SQL Server resource. The sample also declares an
`application_topology` database, which projects as a provider-managed
`application.sql-database` child and appears in the SQL Server **Databases**
tab. When SQL Server starts, the local provider creates the declared database
if it is missing. The backend API references SQL Server through CloudShell
service discovery and exposes `/database`, which opens a SQL connection to the
declared database and executes a small timestamp query. The frontend calls both
`/message` and `/database` through the API so the sample exercises
frontend-to-API and API-to-SQL dependencies.

The backend API also receives Configuration Store and Secrets Vault references
as environment variables. `/settings` returns the configured message, mode,
and whether the secret value was injected without returning the secret itself.
The API reads `ApplicationTopology:SqlServer:ResourceName` so the same project
can target the old provider-owned SQL Server resource or the graph-backed SQL
Server resource through service discovery.
The sample provisions the backend API and SQL Server built-in development
resource identities on startup. It grants the API identity read access to the
Configuration Store and Secrets Vault resources, and records a database
read/write grant on the SQL Server resource. The API is configured to request
brokered SQL credentials from CloudShell at runtime, so `/database` exercises
the CloudShell resource identity, grant check, provider-owned SQL login/user
reconciliation, and normal `SqlConnection` flow. The API registers
`AddCloudShellSqlServerClient(...)` and injects `CloudShellSqlConnectionFactory`
so endpoint code does not construct the broker resolver directly.

## Run

From the repository root:

```bash
dotnet run --project samples/ApplicationTopology/Host -- --urls http://localhost:5104
```

Open:

```text
http://localhost:5104/resources
```

Start the `application-topology-sql-server` resource, then start the
`Application Topology API` resource, then start the `Application Topology
Frontend` resource. CloudShell builds project-backed resources before launch
and then runs them with `dotnet run --no-build`, so the API and frontend do not
start competing implicit builds against their shared `ServiceDefaults` project.
Open:

```text
http://localhost:5218/upstream
```

To generate a deliberate failed request from frontend to API, open:

```text
http://localhost:5218/upstream/failure
```

That route calls the API's `/failure` endpoint through CloudShell service
discovery. The API returns an intentional HTTP 500 and writes an error log;
the frontend records the upstream failure and returns HTTP 502. The
ProblemDetails response includes `traceId`, `resourceName`,
`sampleFailureKind`, and `upstreamStatusCode` extension fields so you can copy
the trace ID into Resource Manager.

Use this path to verify the failure loop:

1. Open `/upstream/failure` and note the returned `traceId`.
2. Open Resource Manager and inspect the frontend and API **Traces** tabs.
3. Use the related-log links from the trace view to inspect the frontend
   warning and API error log entries for the same failed request.

The frontend and API also emit `http.server.requests` and
`http.server.duration` metric points to CloudShell for each request when
`CLOUDSHELL_METRIC_INGEST_ENDPOINT` is configured by the host. Open
`/observability/metrics` or the frontend/API **Metrics** tabs after exercising
`/upstream`, `/upstream/failure`, or `/upstream/fallback` to inspect the
resource-scoped metric points. The host `appsettings.json` maps those standard
request metrics onto basic **Graphs** panels for the frontend and API
resources, while the **Stream** subview keeps the retained raw metric points
available for inspection. The sample also opts into database-backed telemetry
history under `Observability:Telemetry` so traces and metric points remain
available after restarting the CloudShell host. Its appsettings set
`Observability:Telemetry:Store` to `Database`,
`Observability:Telemetry:RetainedSpansPerResource` to `5000`, and
`Observability:Telemetry:RetainedMetricPointsPerResource` to `10000`.

The sample disables startup autostart for these three application resources so
you can exercise the live dependency path deliberately from Resource Manager.

You can override the SQL Server development password with:

```json
{
  "ApplicationTopology": {
    "ConfigurationServiceBasePort": 5138,
    "GraphConfigurationServiceBasePort": 5139,
    "SecretsServiceBasePort": 6138,
    "GraphSecretsServiceBasePort": 6139,
    "SqlServer": {
      "Password": "Your-strong-dev-password!",
      "Port": 14334
    }
  }
}
```

Health checks are cached by the Control Plane so UI instances do not probe the
application services directly. By default, the local development host keeps the
latest health state only. To retain health snapshots in the Resource Manager
database configured by `Persistence`, opt in with:

```json
{
  "ResourceManager": {
    "Health": {
      "SnapshotStore": "Database",
      "RetainedSnapshotsPerResource": 500
    }
  }
}
```

Runtime state is stored under `samples/ApplicationTopology/Host/Data/` and is
ignored by git.

## Configuring Local Hostnames

CloudShell can expose applications through generated hostnames such as:

```text
app.application-topology.cloudshell.local
```

The sample declares a CloudShell Local DNS zone that uses the `local-hostnames`
publisher. Open the `Application Topology Local DNS` resource in Resource
Manager and run **Reconcile name mappings** to apply or re-apply the expected
local host mappings.

By default the local host-name publisher targets the system hosts file, which
may require elevated permissions. To inspect the generated entries without
changing the system hosts file, set `CLOUDSHELL_LOCAL_HOSTS_FILE` before
running the sample:

```bash
CLOUDSHELL_LOCAL_HOSTS_FILE=samples/ApplicationTopology/Host/Data/cloudshell.hosts \
  dotnet run --project samples/ApplicationTopology/Host -- --urls http://localhost:5104
```

### macOS and Linux

When using the default system hosts file target, the publisher writes entries
equivalent to:

```text
127.0.0.1 app.application-topology.cloudshell.local
```

The action may need to run with permissions that can update `/etc/hosts`.

### Windows

When using the default system hosts file target, the publisher writes entries
equivalent to:

```text
127.0.0.1 app.application-topology.cloudshell.local
```

The action may need to run with permissions that can update
`C:\Windows\System32\drivers\etc\hosts`.

### Why is this required?

The operating system must be able to resolve the hostname to an IP address
before a browser can connect to the application. The current local development
publisher materializes exact host mappings through the hosts file. Wildcard
suffixes and public DNS propagation remain provider-specific follow-up work.

## MVP Direction

This sample is the broad local-development MVP proof for CloudShell. Keep the
frontend/backend split: the frontend stays a separate ASP.NET Core project
that calls the backend API through CloudShell service discovery, while the
backend API exercises downstream platform services.

Already covered by the sample:

- Project-backed frontend and API resources that share ServiceDefaults.
- SQL Server as a service resource with a mounted local-storage volume, a
  declared database child, and a local container-backed runtime.
- Configuration Store and Secrets Vault references injected into the API
  without leaking secret values.
- Side-by-side graph-backed Configuration Store and Secrets Vault runtime
  services with seeded provider-owned data and Resource Manager-declared graph
  identity grants for the graph API. The graph API consumes those values
  through the CloudShell Configuration Store and Secrets Vault client
  integrations instead of direct environment-value injection.
- Built-in development resource identity and access grants for settings,
  secrets, and SQL Server database access.
- Local DNS/name mapping through the `local-hostnames` publisher.
- Resource health checks, logs, traces, and the intentional failed request
  path.
- CloudShell-ingested telemetry metrics for frontend and API HTTP request
  counts and durations.
- Smoke-tested runtime correlation for the API `/failure` response and the
  frontend `/upstream/failure` response, including trace IDs and ProblemDetails
  fields that point back to the failing resources.
- Optional smoke-tested SQL-inclusive runtime path, gated on Docker and the
  local SQL Server image, that starts the frontend with dependencies and
  verifies frontend-to-API, settings, secrets, old-provider API-to-SQL
  connectivity, graph API-to-graph-SQL credential connectivity, and graph
  frontend-to-graph-API upstream connectivity.

Remaining useful additions:

- Harden the experimental SQL credential broker with rotation cleanup,
  provider-owned revocation reconciliation, and explicit credential lifetime
  diagnostics.
- Optional container-app variants for the frontend and API only when they prove
  a distinct local-development workflow instead of duplicating the
  project-backed path.
