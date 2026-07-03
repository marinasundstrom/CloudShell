# CloudShell Project Reference Sample

This sample mirrors the Aspire-style local dev loop where one project resource
references another project resource. The C# sample now uses a launcher AppHost
to declare the resource graph and applies that graph to
`CloudShell.LocalDevelopmentHost`; it does not define a full CloudShell host.
Host profile settings, including local authentication and data directory
defaults, live in `AppHost/appsettings.json` and are forwarded to the local
development host by the launcher.

`CloudShell.ProjectReferenceAppHost` declares two ASP.NET Core project
resources through the Resource model provider:

- `Project Reference API` on `http://localhost:5229`
- `Project Reference Frontend` on `http://localhost:5230`

Those resources use the `application.aspnet-core-project` resource type
provider and provider-owned process runtime controller. The frontend declares a
provider-owned resource reference to the API. The ASP.NET Core runtime resolver
derives Aspire-style service discovery configuration from that reference, the
API's `project.serviceDiscoveryName`, and the API endpoint request.

The frontend resource uses:

```csharp
.WithReference(api)
```

Both resources also configure telemetry ingestion explicitly:

```csharp
.WithEnvironmentVariable("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint)
.WithEnvironmentVariable("CLOUDSHELL_METRIC_INGEST_ENDPOINT", metricIngestEndpoint)
```

For example, running the launcher against `http://localhost:5104` injects span
summaries through:

```text
http://localhost:5104/api/control-plane/v1/traces/ingest
```

It also posts basic ASP.NET Core request metric points through:

```text
http://localhost:5104/api/control-plane/v1/metrics/ingest
```

To see traces, run the launcher, start the API and frontend resources from
Resource Manager, open `http://localhost:5230/upstream`, then open
`/telemetry/traces`. To see metrics, open the resource detail page for the API
or frontend and select `Telemetry` / `Metrics`, or open `/telemetry/metrics`.

Both projects reference `CloudShell.ProjectReference.ServiceDefaults`, similar
to an Aspire ServiceDefaults project. It registers common health endpoints,
HTTP client defaults, `Microsoft.Extensions.ServiceDiscovery`, and a
`AddResourceHttpClient(...)` helper that uses Aspire-style logical URIs such as
`https+http://project-reference-api`.

## Run

From this directory:

```bash
./cloudshell.sh run
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

You can also print or apply the launcher-authored template directly:

```bash
./cloudshell.sh template
./cloudshell.sh start
```

The sample omits the old application-provider project records and the old
application provider registration. Smoke coverage starts the launcher-backed
sample and verifies the frontend resolves and calls the API without old
provider records.
