# CloudShell Project Reference Sample

This sample mirrors the Aspire-style local dev loop where one project resource
references another project resource.

`CloudShell.ProjectReferenceHost` declares two ASP.NET Core project resources
through the Resource model provider:

- `Project Reference API` on `http://localhost:5229`
- `Project Reference Frontend` on `http://localhost:5230`

Those resources use the new `application.aspnet-core-project` resource type
provider and provider-owned process runtime controller. The frontend keeps the
same frontend code and declares a provider-owned resource reference to the API.
The ASP.NET Core runtime resolver derives
the Aspire-style service-discovery configuration from that reference, the
API's `project.serviceDiscoveryName`, and the API endpoint request. This
intentionally stays narrow: it proves Resource Manager can list Resource model
resources, dispatch Start to the new provider seam, and compose one
project with another without adapting the old application-provider
definition/store concepts.

The frontend resource uses:

```csharp
.WithReference(api)
```

Both resources also configure telemetry ingestion explicitly:

```csharp
.WithEnvironmentVariable("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint)
.WithEnvironmentVariable("CLOUDSHELL_METRIC_INGEST_ENDPOINT", metricIngestEndpoint)
```

The host reads `Observability:OtlpProtocol` from `appsettings.json` and derives
the local CloudShell endpoint from `--urls`, `ASPNETCORE_URLS`, or
`Observability:Endpoint`. You can still override `Observability:OtlpEndpoint`,
`Observability:TraceIngestEndpoint`, or `Observability:MetricIngestEndpoint`
when the telemetry collector is not the current host.

For example, running the host on `http://localhost:5011` injects:

```text
http://localhost:5011
```

The sample ServiceDefaults project instruments ASP.NET Core, HttpClient, and
the sample `CloudShell.ProjectReference` activity source. It posts span
summaries back to CloudShell through:

```text
http://localhost:5011/api/control-plane/v1/traces/ingest
```

It also posts basic ASP.NET Core request metric points, including request count
and duration, through:

```text
http://localhost:5011/api/control-plane/v1/metrics/ingest
```

To see traces, run the host, start the API and frontend resources from Resource
Manager, open `http://localhost:5230/upstream`, then open
`/observability/traces`. The trace page refreshes while it is open. CloudShell
keeps these spans in memory while the host is running. To see metrics, open
the resource detail page for the API or frontend and select `Telemetry` /
`Metrics`, or open `/observability/metrics`. CloudShell still writes stdout and
stderr to the resource Logs view independently of telemetry collection.

The expected trace includes spans from both web services:

- the frontend request span
- `frontend.call-project-reference-api`
- the outbound HttpClient span
- the API request span
- `api.prepare-message`

This sample is the current proving ground for an Aspire-like distributed trace
experience in CloudShell: start from standard OpenTelemetry instrumentation,
keep spans correlated to CloudShell resources, and evolve the UI toward a
service-aware trace view over time.

Both projects reference `CloudShell.ProjectReference.ServiceDefaults`, similar
to an Aspire ServiceDefaults project. It registers common health endpoints,
HTTP client defaults, `Microsoft.Extensions.ServiceDiscovery`, and a
`AddResourceHttpClient(...)` helper that uses Aspire-style logical URIs such as
`https+http://project-reference-api`. The frontend resource explicitly calls
`WithServiceDiscovery()` so CloudShell injects service discovery configuration
for its referenced API resource. `WithReference(...)` also enables service
discovery for project resources today, but the sample keeps the requirement
visible in the declaration.

CloudShell builds each project before launch and then starts it with
`dotnet run --no-build` by default. The API declares a local HTTP endpoint.
The frontend receives that resolved endpoint through Aspire-compatible service
discovery environment variables and uses a
named `HttpClient` registered from the resource reference. The logical client
URI is resolved by the service discovery handler at request time. Set
`hotReload: true` on a project declaration to opt into non-interactive
`dotnet watch`.

## Run

From the repository root:

```bash
dotnet run --project samples/ProjectReference/Host -- --urls http://localhost:5104
```

Any local HTTP URL works. For example:

```bash
dotnet run --project samples/ProjectReference/Host -- --urls http://localhost:5011
```

Open:

```text
http://localhost:5104/resources
```

Start the `Project Reference API` resource, then start the
`Project Reference Frontend` resource. Open:

```text
http://localhost:5230/upstream
```

The response includes the resolved API endpoint and the API health payload.

You can also open `http://localhost:5229/health` to verify the API directly.
The frontend response should resolve and call the API endpoint.

The ASP.NET Core reference-provider bridge projects provider-observed runtime
state for these Resource model resources. `Unknown` remains the generic fallback
for lifecycle-capable graph resources when no runtime state provider is
registered, but this sample now exercises the provider-local state projection
path for start/stop and health/liveness checks.

The sample omits the old application-provider project records and the old
application provider registration. Smoke coverage starts both project resources
and verifies the frontend resolves and calls the API without old provider
records.

Runtime state is stored under `samples/ProjectReference/Host/Data/`
and is ignored by git.

## Sample Direction

This sample remains the focused ASP.NET Core project-reference baseline for
service discovery, project dependencies, structured logs, and distributed
tracing and metrics. The broader MVP application-topology work has moved to
the forked `samples/ApplicationTopology` sample so this baseline can stay small
and easy to diagnose.
