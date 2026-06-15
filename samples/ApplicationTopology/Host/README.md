# CloudShell Application Topology Sample

This sample is the broad MVP application-topology sample. It was forked from
the focused Project Reference sample so the original can remain a small
service-discovery and distributed-tracing baseline while this sample grows into
the full local development scenario.

The host currently declares:

- `Application Topology API` with an auto-assigned HTTP endpoint
- `Application Topology Frontend` on `http://localhost:5218`
- `Application Topology Local Storage`, backed by `./Data/storage`
- `Application Topology SQL Data`, a volume under the local storage resource
- `application-topology-sql-server`, a SQL Server container app with the data
  volume mounted at `/var/opt/mssql`

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
`localhost:14334`, and mounts a Local Storage-backed volume so database files
can survive restarts of the SQL Server resource. The backend API references
SQL Server through CloudShell service discovery and exposes `/database`, which
opens a SQL connection and executes a small timestamp query. The frontend
calls both `/message` and `/database` through the API so the sample exercises
frontend-to-API and API-to-SQL dependencies.

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
Frontend` resource. Open:

```text
http://localhost:5218/upstream
```

You can override the SQL Server development password with:

```json
{
  "ApplicationTopology": {
    "SqlServer": {
      "Password": "Your-strong-dev-password!"
    }
  }
}
```

Runtime state is stored under `samples/ApplicationTopology/Host/Data/` and is
ignored by git.

## MVP Direction

This sample is intended to become the full-fidelity local development sample
for CloudShell. Keep the frontend/backend split: the frontend should stay a
separate ASP.NET Core project that calls the backend API through CloudShell
service discovery, while the backend API becomes the place where downstream
platform services are exercised.

Planned capabilities to add here:

- Identity-backed SQL Server authentication, so the API can use its CloudShell
  resource identity to access the database in an Azure-like flow.
- Configuration Store and Secrets Vault references consumed by the backend API.
- Resource identity and scoped grants for protected configuration and secret
  access when enforcement is enabled.
- Structured logs from both projects, including fields that correlate to
  traces and resources.
- OpenTelemetry traces across frontend, backend, and downstream service calls,
  visible in the CloudShell traces experience.
- Container-app and networking variants once those primitives are stable enough
  for the sample to demonstrate composition instead of platform gaps.
