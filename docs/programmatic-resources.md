# Programmatic Resources

CloudShell resources can be declared in code as an alternative to the Add
Resource UI. Declarations are a Control Plane concern: install the providers you
need in the Control Plane host, then declare provider-specific resources inside
`Resources`. This lets a host check in its baseline configuration
instead of relying on every developer or operator to add the same resources by
hand.

```csharp
var controlPlane = builder
    .AddCloudShell()
    .AddConfigurationProvider()
    .AddApplicationProvider()
    .UseDocker();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:example", "Example Configuration")
        .WithEntries(
        [
            new("SampleMessage", "Hello from CloudShell configuration"),
            new("SampleSecret", "local-development-secret", IsSecret: true)
        ]);
});
```

Provider packages expose specialized extension methods for their own resource
types. Built-in methods include:

- `AddNetwork(...)` and `AddService(...)` from the core Resource Manager.
- `AddConfigurationStore(...)` from `CloudShell.Providers.Configuration`.
- `AddExecutable(...)`, `AddExecutableApplication(...)`,
  `AddAspNetCoreProject(...)`, and `AddAspNetCoreProjectFromName(...)` from
  `CloudShell.Providers.Applications`. Executable builders configure command
  execution; project builders can also attach container execution metadata.
- `AddContainer(...)` from `CloudShell.Providers.Applications`.
- `AddDocker(...)` from `CloudShell.Providers.Docker` when Docker should be an
  explicit managed resource.

Common workload builder contracts live in `CloudShell.Abstractions`.
`IResourceDeclarationBuilder` is the provider-facing declaration entry point
for `ConfigureResources(...)` and `AddResources(...)`. `IResourceBuilder` is
the common graph builder returned by generic declarations.
`IExecutableResourceBuilder`, `IProjectResourceBuilder`, and
`IContainerResourceBuilder` describe authoring affordances for executable,
project, and container-backed resources. Provider packages still own the
extension methods and builder implementations that translate those calls into
uniform `Resource` projections plus provider-owned configuration.

## Declarative Resource Graph

Programmatic resources can also be used in an Aspire-like style for local
development. In this workflow, resource declarations return builder objects that
can be passed to executable applications. This keeps resource relationships
strongly connected in code instead of repeating string IDs at each call site.
Builders are declaration-time abstractions. They create uniform `Resource`
projections plus provider-owned configuration; they are not runtime resource
subclasses.

Declaration metadata participates in the same resource model validation as
other resource-management paths. If a builder or low-level declaration assigns a
`ResourceClass` for a known resource type, that class must match the class
declared by the resource type. Resource Manager reports mismatches as resource
model diagnostics and keeps the projected `Resource` normalized to the known
type class.

CloudShell uses the same terminology across providers for resource graph
relationships:

- `DependsOn(resource)` is the standard dependency relationship. It records that
  one resource depends on another resource for topology or ordering.
- Provider builders can expose parent-child APIs such as
  `resources.AddDocker().AddContainer(...)` when a resource is owned by another
  resource.
- `WithParent(resource)` records ownership or containment. Parent-child
  relationships affect how resources are grouped in Resource Manager; they are
  separate from dependency relationships.
- String IDs remain available as a lower-level escape hatch, but typed builders
  should be preferred when both resources are declared in the same callback.

Executable applications and ASP.NET Core projects have one additional
Aspire-compatible concept: endpoint references. `WithReference(resource)`
records that the application wants endpoint/configuration values for another
resource. For ASP.NET Core project resources, `WithReference(...)` also enables
service discovery configuration for the referenced resource. For generic
executable applications, `WithServiceDiscovery()` remains the explicit opt-in
that maps referenced resource endpoints into the .NET configuration shape. This
keeps CloudShell open to other service discovery mechanisms, such as a dedicated
service discovery service running in a container.

Executable applications also keep `WaitFor(resource)` as an Aspire-compatible
alias for dependency ordering. Prefer `DependsOn(resource)` when describing the
CloudShell resource graph.

Application resources also expose basic Aspire-compatible observability. When
enabled, CloudShell marks the resource as log-, trace-, and metric-capable and
injects standard `OTEL_*` variables when the resource starts. Generated
observability variables are applied before explicit resource environment
variables, so `WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "...")` can
override provider defaults for a single resource.

```csharp
resources
    .AddAspNetCoreProject(
        "application:api",
        "API",
        "src/API/API.csproj")
    .WithOtlpExporter("http://localhost:4317");

resources
    .AddContainer("worker", "example/worker:dev")
    .WithObservability(false);
```

Applications can depend on any declared resource builder, including sub-resources
such as containers returned from `resources.AddDocker().AddContainer(...)`.

CloudShell also includes logical network and service resources. A network is an
orchestration boundary. A service is a stable endpoint over one or more target
resources, with explicit private, local, network, or public exposure. With the
default orchestrator, CloudShell assumes the host environment owns networking
and projects services as host-local endpoints. If a service port omits `port`,
CloudShell assigns a stable local port automatically. Orchestrator extensions
can translate the same declarations to Docker Compose networks and published
ports, or another runtime-specific model.

Provider-specific resources should stay logical at the declaration site. For
example, a future SQL Server provider should be able to expose a top-level
resource without making the caller choose a Docker host:

```csharp
cloudShell.Resources(resources =>
{
    resources.AddSqlServer("sql:main", "Main SQL Server");
});
```

That SQL Server provider can describe the resource as a container-backed
workload internally. Top-level container applications use the same shape:

```csharp
cloudShell.Resources(resources =>
{
    resources
        .AddContainer("redis", "redis:7.2")
        .WithImage("redis:7.2-alpine");
});
```

The selected orchestrator and preferred container engine decide whether that
workload runs through Docker Compose or another runtime. The engine is
infrastructure selection, not part of the logical resource parentage.

```csharp
var configuration = resources.AddConfigurationStore(
    "configuration:example",
    "Example Configuration");

var database = resources.Declare("managed", "postgres-main");
var redis = resources
    .AddDocker()
    .AddContainer("redis", "redis", "7.2")
    .DependsOn(database);

resources
    .AddAspNetCoreProject(
        "application:example-web-api",
        "Example Web API",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .AsContainerImage("example-web-api:dev")
    .WithReference(configuration)
    .WithReference(redis)
    .DependsOn(redis);

var appNetwork = resources.AddNetwork("network:app", "App Network");

resources
    .AddService("service:example-web-api", "Example Web API")
    .Targets("application:example-web-api")
    .WithNetwork(appNetwork)
    .WithPort(
        "http",
        targetPort: 5127,
        port: 8080,
        protocol: "http",
        exposure: ResourceExposureScope.Public);
```

ASP.NET Core project declarations run with hot reload by default:

```bash
dotnet watch --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj run --no-launch-profile
```

Set `hotReload: false` when you want a plain `dotnet run` process. ASP.NET Core
project resources get a stable local HTTP endpoint automatically when `endpoint`
is omitted. Supplying `endpoint: "http://localhost:5127"` fixes the port instead.
CloudShell injects the resolved endpoint into `ASPNETCORE_URLS`, so the project
binds to the Resource Manager endpoint without relying on launch profiles.
Use `WithHttpEndpoint(...)`, `WithHttpsEndpoint(...)`, or
`WithEndpointPort(...)` to declare fixed or named endpoints. Named endpoints
match the Aspire URI shape `https+http://_endpointName.serviceName`.

`AddDocker()` declares the default local Docker Engine resource. Containers are
declared from that Docker resource with `AddContainer(name, image, tag)`,
following the Aspire-style logical name shape. CloudShell derives the stable
container resource ID as `docker:container:<name>` and declares the container as
a sub-resource of the Docker resource that created it.

Use `DependsOn(...)` to add topology dependencies without changing the
container's parent relationship to Docker. When you need a custom container
resource ID or display name, use `AddDocker().AddDockerContainer(id, name,
image)`. To model more than one Docker parent, use `AddDocker(id, name)` and
add containers from the returned Docker resource builder:

```csharp
var devDocker = resources.AddDocker("docker:dev", "Development Docker");
var testDocker = resources.AddDocker("docker:test", "Test Docker");

var devRedis = devDocker.AddContainer("redis-dev", "redis", "7.2");
var testRedis = testDocker.AddContainer("redis-test", "redis", "7.2");
```

When the application starts, CloudShell maps referenced resource endpoints into
the .NET configuration shape used for service discovery, which provides a level
of compatibility with Aspire applications:

```text
services__<resource-name-or-id>__<endpoint-name-or-scheme>__0=<endpoint-address>
```

Endpoint variables are emitted only for referenced resources registered in the
same resource group as the application. Explicit application environment
variables are applied after generated values, so they can override generated
endpoint variables when needed.

Applications can consume the generated values through normal `IConfiguration`.
The reusable `CloudShell.Configuration` package also includes small helpers for
HttpClient-style setup:

```csharp
var endpoint = builder.Configuration.GetResourceUri(
    "configuration-example",
    "entries");

var managementEndpoint = builder.Configuration.GetResourceUri(
    "rabbitmq",
    "management");

if (endpoint is not null)
{
    builder.Services.AddHttpClient("configuration", client =>
    {
        client.BaseAddress = endpoint;
    });
}
```

The generic `IResourceBuilder` still supports string IDs as a lower
level escape hatch, but typed builders should prefer resource-builder overloads
for dependencies, endpoint references, and provider-specific relationships.
Generic builders can also attach broad projection metadata with
`WithResourceClass(...)`, `WithResourceAttribute(...)`, and
`WithResourceAttributes(...)`. These are declaration-time hints for stable,
non-secret class and attribute data; provider-owned configuration and runtime
state still belong behind provider contracts. UI and API creation flows can
carry the same metadata to creation providers through `CreateResourceCommand`
and `ResourceCreationRequest`.
When a provider exposes executable, project, or container-backed declarations,
prefer returning the shared workload builder interfaces from
`CloudShell.Abstractions` instead of defining provider-local resource subclasses
or duplicate builder contracts.

The host sample declares only `Example Configuration` programmatically. Other
resources are expected to be added through the Resource Manager UI unless a host
chooses to declare more of them in code.

## Runtime Behavior

Programmatic declarations are registered when CloudShell starts. They appear in
Resource Manager, participate in authorization, can be assigned to a resource
group with `WithResourceGroup(...)`, and can declare dependencies with
`DependsOn(...)` or provider-specific dependency methods.

Dependency auto-start is enabled by default for programmatic resource graphs.
When a Run action is executed with dependency startup enabled, CloudShell starts
stopped dependencies before starting the requested resource. This matches the
Aspire-style expectation that a declared application graph can be run from the
top-level application.

Configure the graph default with `resources.WithAutoStart(...)`:

```csharp
controlPlane.Resources(resources =>
{
    resources.WithAutoStart(false);

    var database = resources
        .AddContainer("postgres", "postgres:16-alpine")
        .WithAutoStart(true);

    resources
        .AddAspNetCoreProject(
            "application:api",
            "API",
            "src/Api/Api.csproj")
        .DependsOn(database);
});
```

The graph default is inherited by resources that do not set their own value.
Use `WithAutoStart(false)` on a resource when it should not be started
automatically as a dependency, such as an expensive local database or an
externally managed service. Explicitly running that resource still works. If a
resource depends on a stopped dependency whose effective auto-start setting is
disabled, CloudShell stops the action with a clear error instead of silently
starting that dependency.

`WithAutoStart(...)` does not mean "start every declared resource when
CloudShell starts." It only controls whether stopped dependencies may be started
as part of another resource's lifecycle action.

Lifecycle execution is selected through `IResourceOrchestrator`. CloudShell
ships a default orchestrator that preserves the current provider-backed
behavior and treats service exposure as host-local. Extensions can add
orchestrators such as Docker Compose and translate the same resource graph,
network, service, and exposure descriptors into runtime-specific artifacts.

The Docker Compose orchestrator runs lifecycle commands through `docker compose`
for matching Compose service names. For example, `application:api` maps to the
`api` Compose service. `UseDocker(...)` registers Docker as an implicit
container engine and enables Docker Compose orchestration without adding Docker
to the resource graph. Use `UseContainerEngine(...)` to register another
Docker-compatible or Podman-compatible engine directly.

When a project uses `AsContainerImage(...)` or `WithContainerBuild(...)`, or a
resource is declared through `AddContainer(...)`, it can be materialized into
generated Compose YAML. A plain local executable without container image or
build metadata remains a default-orchestrator workload. Container-backed
resource builders expose `WithImage(...)` so provider-specific resources such as
`AddSqlServer(...)` can let callers override their default image without
exposing Docker in the logical graph. The Resource Manager settings can record a
preferred container engine, but application and service declarations do not need
to be tied to a specific engine in the user-facing graph.

The explicit Docker resource model remains available separately. Use
`AddDockerProvider()` plus `resources.AddDocker()` when Docker itself should
appear as a managed resource with child container resources. It can coexist with
an implicit default engine; CloudShell does not enforce that both point at the
same environment.

The active orchestrator can be changed from Resource Manager settings. Changing
orchestrators does not migrate existing runtime state; the selected orchestrator
is used for future lifecycle actions when it can handle the target resource, and
CloudShell falls back to the default orchestrator otherwise.

By default, programmatic resources are not persisted. The code declaration is
the source of truth for the current process. If the declaration is removed from
startup code, the resource no longer appears after restart unless it was
persisted separately.

Use `Persist()` when the declaration should materialize into provider-owned
configuration and the core resource registration store using the same provider
setup logic as the UI:

```csharp
controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:shared", "Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true")
        .Persist();
});
```

Persisted declarations are written during startup after the CloudShell database
has been initialized. After that, the resource can continue to exist even if the
declaration is removed from code.

`Persist()` does not overwrite an existing persisted resource. Use
`Persist(overwrite: true)` when checked-in configuration should replace the
current persisted provider configuration and registration metadata:

```csharp
controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:shared", "Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true")
        .Persist(overwrite: true);
});
```

## Provider Boundary

Programmatic resources follow the same persistence boundary as UI-created
resources:

- CloudShell owns the core resource registration, group assignment, and
  dependency metadata.
- Providers own resource-specific configuration such as executable command
  settings or configuration entries.

`Persist()` writes both sides through their existing stores. Without `Persist()`,
provider-specific configuration is kept in memory for the current process.

In split deployments, keep this API in the Control Plane host. The UI host
should discover resources through the Control Plane API rather than declaring
resources itself.

See [Hosting model](hosting-model.md).
