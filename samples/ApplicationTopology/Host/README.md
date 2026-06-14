# CloudShell Application Topology Sample

This sample is the broad MVP application-topology sample. It was forked from
the focused Project Reference sample so the original can remain a small
service-discovery and distributed-tracing baseline while this sample grows into
the full local development scenario.

The host currently declares two ASP.NET Core project resources:

- `Application Topology API` with an auto-assigned HTTP endpoint
- `Application Topology Frontend` on `http://localhost:5218`

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

## Run

From the repository root:

```bash
dotnet run --project samples/ApplicationTopology/Host -- --urls http://localhost:5104
```

Open:

```text
http://localhost:5104/resources
```

Start the `Application Topology API` resource, then start the
`Application Topology Frontend` resource. Open:

```text
http://localhost:5218/upstream
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

- SQL Server as a stateful dependency, backed by Local Storage and a mounted
  data volume.
- Configuration Store and Secrets Vault references consumed by the backend API.
- Resource identity and scoped grants for protected configuration and secret
  access when enforcement is enabled.
- Structured logs from both projects, including fields that correlate to
  traces and resources.
- OpenTelemetry traces across frontend, backend, and downstream service calls,
  visible in the CloudShell traces experience.
- Container-app and networking variants once those primitives are stable enough
  for the sample to demonstrate composition instead of platform gaps.
