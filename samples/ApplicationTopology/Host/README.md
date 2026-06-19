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
- `application-topology-sql-server`, a SQL Server container app with the data
  volume mounted at `/var/opt/mssql`
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

Both projects use the shared `ServiceDefaults` project for health endpoints,
HTTP client service discovery, JSON console logs, and OpenTelemetry tracing.
The host injects CloudShell trace ingestion settings so `/observability/traces`
can show spans across both services.

The SQL Server resource is intentionally sample-local composition over the
current container app primitives. It uses Docker through
`UseLocalDevelopmentDefaults()`, publishes a local `tds` endpoint on
`localhost:14334` by default, and mounts a Local Storage-backed volume so
database files can survive restarts of the SQL Server resource. The backend
API references SQL Server through CloudShell service discovery and exposes
`/database`, which opens a SQL connection and executes a small timestamp
query. The frontend calls both `/message` and `/database` through the API so
the sample exercises frontend-to-API and API-to-SQL dependencies.

The backend API also receives Configuration Store and Secrets Vault references
as environment variables. `/settings` returns the configured message, mode,
and whether the secret value was injected without returning the secret itself.
The sample provisions the backend API's built-in development resource identity
on startup and grants that identity read access to the Configuration Store and
Secrets Vault resources.

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
the frontend records the upstream failure and returns HTTP 502. Use this path
to inspect failed request telemetry, traces, and correlated application logs.

The sample disables startup autostart for these three application resources so
you can exercise the live dependency path deliberately from Resource Manager.

You can override the SQL Server development password with:

```json
{
  "ApplicationTopology": {
    "ConfigurationServiceBasePort": 5138,
    "SecretsServiceBasePort": 6138,
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

This sample is intended to become the full-fidelity local development sample
for CloudShell. Keep the frontend/backend split: the frontend should stay a
separate ASP.NET Core project that calls the backend API through CloudShell
service discovery, while the backend API becomes the place where downstream
platform services are exercised.

Planned capabilities to add here:

- Identity-backed SQL Server authentication, so the API can use its CloudShell
  resource identity to access the database in an Azure-like flow.
- Structured logs from both projects, including fields that correlate to
  traces and resources.
- OpenTelemetry traces across frontend, backend, and downstream service calls,
  visible in the CloudShell traces experience.
- Container-app and networking variants once those primitives are stable enough
  for the sample to demonstrate composition instead of platform gaps.
