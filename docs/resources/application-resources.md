# Application Resources

CloudShell includes an application provider for local development machines. It
projects host-local workloads into Resource Manager while keeping provider-owned
configuration and runtime state behind the provider boundary.

Resource-type-specific guidance:

- [Executable applications](executable-applications.md) for
  `application.executable` command resources.
- [ASP.NET Core applications](aspnet-core-applications.md) for
  `application.aspnet-core-project` project resources.
- [Container apps](container-apps.md) for `application.container-app` deployable
  container workloads.

Application resources are primarily intended for local development:
ASP.NET Core APIs, frontend dev servers, emulators, workers, containerized
services, and similar host-local tools. They are not a deployment abstraction
for remote infrastructure.

## Shared Runtime State

The provider persists runtime state separately from application configuration.
By default:

```text
CloudShell.Host/Data/application-resources.json
CloudShell.Host/Data/application-runtime-state.json
CloudShell.Host/Data/application-logs/
```

The runtime state file stores process/container observations such as the last
known process ID, observed process start time, last observation time, last exit
code, and log path when those concepts apply. The `Data` directory is ignored by
git because this is local machine state.

## Resource Templates

The application provider supports resource templates for
`application.executable`, `application.aspnet-core-project`,
`application.container-app`, and `application.sql-server` resources. Export
writes a provider-owned configuration payload with the resource-type-specific
configuration, such as:

- executable path, arguments, and working directory
- project path, project application arguments, and ASP.NET Core hot reload mode
- container image, host binding, endpoints, and environment variables
- lifetime and service discovery opt-in where supported

Import creates a new application definition in the provider's configuration
store, assigns it to the imported group, and avoids overwriting an existing
application with the same generated ID.

See [Resource templates](../resource-templates.md).

## Observability

Application resources have Aspire-compatible observability metadata. By default,
executable, ASP.NET Core project, and container app resources declare support
for logs, traces, and metrics. When the resource starts, CloudShell adds
OpenTelemetry environment variables before user-configured environment variables
are applied, so explicit resource variables can override generated values.

CloudShell emits:

```text
OTEL_SERVICE_NAME=<normalized-resource-name>
OTEL_RESOURCE_ATTRIBUTES=service.instance.id=<resource-id>,cloudshell.resource.id=<resource-id>,cloudshell.resource.type=<resource-type>
OTEL_EXPORTER_OTLP_ENDPOINT=<resolved-endpoint>
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
OTEL_EXPORTER_OTLP_HEADERS=<resolved-headers>
```

`OTEL_EXPORTER_OTLP_ENDPOINT` is resolved in this order:

1. The resource's `WithOtlpExporter(...)` endpoint.
2. `ApplicationProviderOptions.OtlpEndpoint`.
3. `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, using `grpc`.
4. `ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL`, using `http/protobuf`.
5. An inherited `OTEL_EXPORTER_OTLP_ENDPOINT`.

Use provider options to set a process-wide collector endpoint:

```csharp
builder
    .AddCloudShell()
    .AddApplicationProvider(options =>
    {
        options.OtlpEndpoint = "http://localhost:4317";
        options.OtlpProtocol = "grpc";
    });
```

Use fluent resource APIs to configure or disable a specific resource:

```csharp
resources
    .AddAspNetCoreProject(
        "application:example-web-api",
        "Example Web API",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithOtlpExporter("http://localhost:4317", protocol: "grpc");

resources
    .AddContainer("redis", "redis:7.2")
    .WithObservability(false);
```

Container-backed application resources include the generated OTEL variables in
their workload descriptor, so generated Docker Compose files receive the same
settings as locally started processes.

## Service Discovery

Application resources can opt in to Aspire-compatible service discovery for
referenced resources. `WithReference(...)` records that an application wants
endpoint/configuration values for another resource; `WithServiceDiscovery()` is
the separate opt-in that maps those referenced resource endpoints into
environment variables using the .NET configuration shape.

ASP.NET Core project resources enable that mapping automatically when
`WithReference(...)` is used.

```text
services__<resource-name>__<endpoint-name-or-scheme>__0=<endpoint-address>
```

CloudShell emits names based on both the referenced resource name and resource
ID, normalized for environment variables. Explicit application environment
variables are applied last, so they can override generated service discovery
variables.

Endpoint variables are generated from the application's referenced resources,
not from its wait dependencies. For declarative application resources,
`WithReference(...)` records an endpoint reference, while `DependsOn(...)`
records a startup dependency. The broader resource model uses `DependsOn(...)`
as the standard dependency relationship; `WaitFor(...)` remains available on the
executable application builder as an Aspire-compatible alias. CloudShell only
emits endpoint variables when the referenced resource is registered in the same
resource group.

An application can depend on any resource builder returned from the declarative
graph, including provider sub-resources such as Docker containers.

Service discovery is intentionally opt-in for generic application resources. An
application can reference or depend on resources without receiving generated
environment variables, which leaves room for other discovery mechanisms such as
a service discovery service running in a container.

Applications can read the generated URLs directly through `IConfiguration`:

```csharp
client.BaseAddress = builder.Configuration.GetResourceUri("example-api", "http");
```
