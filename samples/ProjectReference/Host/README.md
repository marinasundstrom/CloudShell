# CloudShell Project Reference Sample

This sample mirrors the Aspire-style local dev loop where one project resource
references another project resource.

`CloudShell.ProjectReferenceHost` declares two ASP.NET Core project resources:

- `Project Reference API` with an auto-assigned HTTP endpoint
- `Project Reference Frontend` on `http://localhost:5218`

The frontend resource uses:

```csharp
.WithReference(api)
.DependsOn(api)
```

Both resources also enable OTLP export explicitly:

```csharp
.WithOtlpExporter(otlpEndpoint, otlpProtocol)
```

The host reads that endpoint from `Observability:OtlpEndpoint` and
`Observability:OtlpProtocol` in `appsettings.json`. The sample default is:

```text
http://localhost:4317
```

Run an OpenTelemetry collector, Aspire Dashboard, or another OTLP receiver on
that endpoint before starting the resources if you want traces and metrics to
be collected. CloudShell still writes stdout and stderr to the resource Logs
view independently of OTLP export.

Both projects reference `CloudShell.ProjectReference.ServiceDefaults`, similar
to an Aspire ServiceDefaults project. It registers common health endpoints,
HTTP client defaults, `Microsoft.Extensions.ServiceDiscovery`, and a
`AddResourceHttpClient(...)` helper that uses Aspire-style logical URIs such as
`https+http://project-reference-api`. For ASP.NET Core project resources,
`WithReference(...)` enables the referenced project's service discovery
configuration automatically.

CloudShell starts both projects with `dotnet watch` by default. The API omits a
port, so CloudShell assigns a stable local HTTP endpoint. The frontend receives
that resolved endpoint through Aspire-compatible service discovery environment
variables and uses a named `HttpClient` registered from the resource reference.
The logical client URI is resolved by the service discovery handler at request
time.

## Run

From the repository root:

```bash
dotnet run --project samples/ProjectReference/Host --urls http://localhost:5104
```

Open:

```text
http://localhost:5104/resources
```

Run the `Project Reference API` resource, then run the
`Project Reference Frontend` resource. Open:

```text
http://localhost:5218/upstream
```

The response includes the resolved API endpoint and the API health payload.

Runtime state is stored under `samples/ProjectReference/Host/Data/`
and is ignored by git.
