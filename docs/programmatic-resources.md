# Programmatic Resources

CloudShell resources can be declared in code as an alternative to the Add
Resource UI. Declarations are a Control Plane concern: install the providers you
need in the Control Plane host, then declare provider-specific resources inside
`Resources`. This lets a host check in its baseline configuration
instead of relying on every developer or operator to add the same resources by
hand.

For local development, those declarations commonly live in a combined host
application that runs both the Control Plane and the CloudShell UI in one
process. In that shape, declared executable, project, and container-backed
resources can run from the same host process context, but they are still
managed by the same local Control Plane. The declarations and lifecycle policy
belong to the Control Plane; the host process composes the environment and gives
provider implementations local process context to work from.

```csharp
var controlPlane = builder
    .AddCloudShell()
    .AddConfigurationProvider()
    .AddSecretsProvider()
    .AddApplicationProvider()
    .UseDocker();

controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("example")
        .WithDisplayName("Example Configuration")
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
- `AddSecretsVault(...)` from `CloudShell.Providers.Configuration`.
- `AddExecutable(...)`, `AddExecutableApplication(...)`,
  and `AddAspNetCoreProject(...)` from `CloudShell.Providers.Applications`.
  Executable builders configure command execution; project builders can also
  attach container execution metadata.
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

For built-in application resources, those builders feed the shared
application-resource infrastructure. The declaration describes the stable
resource and its runtime intent; the application provider handles common
process and container lifecycle management, log capture, runtime state, and
host-scoped cleanup. A single application resource can be backed by multiple
executables or containers, and provider or orchestrator sub-resources should
remain contained under the application resource unless the user intentionally
models them as independent resources. See
[Application resources](resources/application-resources.md).

Application-like provider packages can reuse the first shared application
provider seam by subclassing `ApplicationResourceTypeProvider` and supplying an
`ApplicationResourceProjection` for their own resource type. The shared
projection source keeps custom application resources on the same declaration,
template, lifecycle, log, monitoring, and orchestration descriptor path as the
built-in application providers while the custom provider keeps its own provider
identity and resource type. The base provider depends on separate
provider-facing contracts for definitions, procedures, templates,
declarations, descriptors, and action availability; specialized providers can
add only the role-specific contracts they need instead of depending on the
whole built-in application provider facade.

The intended end state is a set of composable application-resource primitives:
an external provider declares its stable resource, chooses whether the runtime
is a local executable, ad-hoc container, or managed sub-resource set, and then
uses default services for the common lifecycle, logs, telemetry, endpoint,
health, liveness, configuration, storage, and cleanup behavior. Provider
authors should only implement the parts that make their resource distinct.

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

Later graph serialization work should add an explicit weak reference shape for
provider-projected resources that may not exist in the current graph yet. For
example, a declaration might need to refer to a database name under a SQL
Server resource before the provider has created or inspected that database, or
to a runtime container identity under a container host before the host has
projected it. Those references should not be encoded as `DependsOn(...)` unless
the target resource already exists and should participate in dependency
behavior. A future weak reference should persist owner/provider context, target
type, and target key/name, then let the owning provider validate whether the
unmaterialized target can be referenced and emit diagnostics if it cannot be
resolved.

Programmatic resource APIs take scoped resource names such as `api`,
`orders--api`, or `sample-app`. Providers derive the canonical resource ID
from the name when they need a typed internal path, such as `application:api`.
String IDs remain available as lower-level references for existing resources
and advanced scenarios, but new declarations should treat the authored value
as a resource name unless the API explicitly asks for a resource ID.

The Resource Graph POC is adding a separate `ResourceDefinitionGraphBuilder`
for code-first authoring of graph resources and deployment inputs. These
builders emit `ResourceDefinition` values, not Resource Manager declarations,
so the same authored graph can be serialized, imported, applied as a
deployment, or used by tests. Provider builders should start as small manual
implementations next to the resource type provider they target. Source
generation remains a future option once several manual builders show the
stable conventions and the customization points providers need.

```csharp
var graph = new ResourceDefinitionGraphBuilder();

graph
    .AddNetwork("app")
    .WithDisplayName("App Network")
    .WithHostReadiness("logicalOnly");

var deployment = graph.BuildDeployment("local-app", environmentId: "local");
```

The initial manual builders cover generic networks, Configuration Store, and
Secrets Vault graph resources. The configuration and secrets builders can
declare service endpoints and participate in dependencies, but they
intentionally do not author configuration entries or secret values as graph
attributes. Those values remain provider/runtime data.

Display names are optional presentation labels. Use `.WithDisplayName(...)`
when a local development dashboard or sample benefits from a friendlier label.

Resource names and item names can be structured when users want a logical
hierarchy. See [Naming conventions](naming-conventions.md) for optional
resource name, configuration key, and secret naming guidance.

Executable applications and ASP.NET Core projects have one additional
Aspire-compatible concept: endpoint references. `WithReference(resource)`
records that the application wants endpoint/configuration values for another
resource. For ASP.NET Core project resources, `WithReference(...)` also enables
service discovery configuration for the referenced resource. For generic
executable applications and container apps, `WithServiceDiscovery()` remains
the explicit opt-in that maps referenced resource endpoints into the .NET
configuration shape. This keeps CloudShell open to other service discovery
mechanisms, such as a dedicated service discovery service running in a
container.

Configuration Store and Secrets Vault are ordinary referenced services for
endpoint discovery. Use `WithReference(...)` for discovery, and use identity
bindings plus grants for authorization. Do not treat their endpoint variables as
part of the resource identity credential contract.

Programmatic declarations can also model resource access grants. Use the
typed `ResourceAccessPermissions` profiles when the intended level is
resource reference, read, operate, or manage:

```csharp
var frontend = resources.AddAspNetCoreProject("frontend", "../Frontend/Frontend.csproj");
var api = resources
    .AddAspNetCoreProject("api", "../Api/Api.csproj")
    .WithIdentity("development", name: "api-service");

frontend.Allow(api, ResourceAccessPermissions.Reference);
api.Allow(frontend, ResourceAccessPermissions.Read);
api.Allow(frontend, ResourceAccessPermissions.Operate(
    CommonResourceOperationPermissions.LifecycleAction));
```

`Reference` lets the principal see the target as a locked relationship without
granting inspection. `Read` grants inspection, operation permission sets grant
action-specific operation access, and `Manage` grants administrative resource
management.

Programmatic application declarations default to host-scoped lifetime for local
development. Executable applications, ASP.NET Core projects, and container apps
are stopped with the CloudShell host and reconciled on the next Control Plane
startup. Use `.WithLifetime(ResourceLifetime.Detached)` when a declaration is a
longer-lived service that should keep running after the host exits. UI-created
application resources default detached where supported because those workflows
usually model manually managed or production-like resources.

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
        "api",
        "src/API/API.csproj")
    .WithOtlpExporter("http://localhost:4317");

resources
    .AddContainer("worker", "example/worker:dev")
    .WithObservability(false);
```

Applications can depend on any declared resource builder, including sub-resources
such as containers returned from `resources.AddDocker().AddContainer(...)`.

CloudShell also includes an implied local network, explicit logical and
virtual network resources, and optional service resources. Programmatic
endpoint helpers can keep local development Aspire-compatible by declaring an
application endpoint and producing an endpoint mapping to the implied default
local network. In the current local development topology, that mapping resolves
to a local address such as `localhost:<port>` or `127.0.0.1:<port>`. That
helper behavior is not the canonical networking model.

A resource endpoint describes the named protocol/port exposed by the resource.
Endpoint-network mappings connect that resource endpoint to a network or
topology and provide the concrete address for that topology. When an
Aspire-like helper supplies a host or port, CloudShell should treat that as
input for the endpoint-network mapping. The mapping address is then passed to
the service at start time, while the endpoint remains the resource-owned
protocol/target-port contract. Configured endpoint mappings can also connect a
network-owned endpoint to a target resource endpoint. Exposure resources and
providers then make mapped endpoints reachable across topology boundaries when
needed.

A logical network is a named orchestration boundary. A virtual network is a
richer environment boundary for on-premise or provider-backed networking.
Application resources, especially container apps, are the normal stable
deployment and exposure artifacts for app workloads: they can carry app-owned
endpoints, discovery names, public exposure intent, load-balancer mappings, and
DNS/name mappings. A `cloudshell.service` resource is a CloudShell model/API
concept, not automatically the same thing as the internal orchestrator service
descriptor used to maintain container app replicas. It is an optional
CloudShell resource that can model a stable service unit or facade over
non-application targets, multiple targets, imported provider-native services,
or advanced routing scenarios. For example, a team can manually compose a
replica set from several web application instance resources, put a Service in
front of them, and configure a load balancer to target that Service endpoint.
Orchestrator extensions can translate
application and networking declarations to Docker Compose networks and
published ports, on-premise clusters, or another runtime-specific model without
requiring a `cloudshell.service` resource for normal container app exposure.
When a `cloudshell.service` resource is explicitly modeled, a future
orchestrator may choose to materialize that resource as its provider-native
Service concept or derive its orchestration descriptor from it.

Networks can reserve or request endpoints. Manual endpoint requests carry the
concrete host/IP address and port for that network-owned endpoint. Auto
endpoint requests let the network resource allocate from its configured policy;
the local-development provider can resolve those requests to stable localhost
ports from the Control Plane auto-port range. Endpoint mappings connect a
network-owned endpoint to a target resource endpoint. When no provider is
specified, the network resource itself is the endpoint-mapping provider:

```csharp
var appNetwork = resources
    .AddNetwork("app", isDefault: true)
    .WithDisplayName("App Network");

var publicEndpoint = appNetwork.AddTcpEndpoint(
    "localhost",
    port: 4040,
    name: "public");

var autoEndpoint = appNetwork.RequestHttpEndpoint("api");

appNetwork.MapEndpoint(
    autoEndpoint,
    new ResourceEndpointReference("application:example-web-api", "http"));
```

Virtual networks use the same endpoint request and mapping primitives while
projecting a richer network capability:

```csharp
var appNetwork = resources
    .AddVirtualNetwork("app", isDefault: true)
    .WithDisplayName("App Network");

var publicEndpoint = appNetwork.RequestHttpEndpoint(
    "api",
    exposure: ResourceExposureScope.Public);
```

Networking resources advertise capabilities such as
`networking.endpointProvider` and `networking.endpointMapper`. Authored
resource types can use the same capability model for richer providers, such as
a containerized gateway, reverse proxy, load balancer, DNS publisher, or
network policy controller. A mapping can name one of those resources as the
provider while keeping the logical network as the boundary:

```csharp
var appNetwork = resources
    .AddNetwork("app", isDefault: true)
    .WithDisplayName("App Network");

var api = resources.Declare("applications.aspnet-core-project", "application:example-web-api");
var gateway = resources.Declare("networking", "networking:gateway");
var publicEndpoint = appNetwork.RequestHttpEndpoint("api");

appNetwork.MapEndpoint(
    publicEndpoint,
    new ResourceEndpointReference(api.ResourceId, "http"),
    gateway,
    "mapping:api");
```

The selected provider resource must advertise `networking.endpointMapper`.
The platform network exposes a `reconcileEndpointMappings` action for declared
mappings. That action validates the source endpoint, target endpoint, and
selected provider capability before provider-owned networking software applies
its own routing or policy configuration.

Load balancers are a provider-neutral routing resource built on the same stable
resource model. Use `AddLoadBalancer(...)` when the user-facing resource should
own entrypoints and routes while a provider such as Traefik materializes the
implementation:

```csharp
var lb = resources
    .AddLoadBalancer("public")
    .UseProvider("traefik")
    .ExposeHttp(80)
    .ExposeHttps(443);

lb.MapHost("app.local", webApp, endpoint: "http");
lb.MapPath("api.local", "/v1", apiService, endpoint: "http");
lb.MapTcp(5432, postgres, endpoint: "postgres");
```

See [Load balancers](resources/load-balancers.md).

Provider-specific resources should stay logical at the declaration site. For
example, the built-in SQL Server builder exposes a top-level SQL Server
resource without making the caller declare a generic container app:

```csharp
cloudShell.Resources(resources =>
{
    var sqlData = resources
        .AddVolume("sql-data")
        .WithDisplayName("SQL Data");

    resources
        .AddSqlServer("main", dataVolume: sqlData, port: 14334)
        .DeclareDatabase("appdb", "Application DB")
        .WithDisplayName("Main SQL Server");
});
```

The current local provider still uses a SQL Server container image internally,
but callers receive an `application.sql-server` service resource rather than a
container-app builder. Declared databases project as provider-managed
`application.sql-database` children and appear on the SQL Server resource's
Databases tab. Declaring a database records the assumption that it should exist
on the SQL Server; it is not an operation and does not create it by default.
Local development and test declarations can call
`DeclareDatabase(...).EnsureCreated()` as a separate provider operation request
to create the database if it is missing before access grants are reconciled.
Future SQL Server builder slices should add
validated SQL Server concepts such as version and edition instead of arbitrary
image selection, and should materialize access grants into SQL users, roles, or
provider-specific credentials. Top-level container applications are the place
where image selection is part of the logical declaration:

```csharp
cloudShell.Resources(resources =>
{
    resources
        .AddContainerApplication("redis", "redis:7.2")
        .WithRegistry("http://localhost:5000")
        .WithRegistryCredentialsFromEnvironment("registry-user", "REGISTRY_PASSWORD")
        .WithImage("redis:7.2-alpine");
});
```

`AddContainer(...)` is the Aspire-compatible shorthand for the same top-level
`application.container-app` resource. It is not the Docker child-container API.
Use `resources.AddDocker().AddContainer(...)` only when the Docker container
itself should be modeled as a sub-resource under a Docker resource.

The selected orchestrator and preferred container host decide whether that
workload runs through Docker Compose or another runtime. The host is an
instance of a runtime or control boundary; Docker, Podman, and similar runtime
types are host facts rather than logical resource parentage.

```csharp
var configuration = resources
    .AddConfigurationStore("example")
    .WithDisplayName("Example Configuration");

var database = resources.Declare("managed", "postgres-main");
var redis = resources
    .AddDocker()
    .AddContainer("redis", "redis", "7.2")
    .DependsOn(database);

resources
    .AddAspNetCoreProject(
        "example-web-api",
        "samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
    .WithDisplayName("Example Web API")
    .AsContainer()
    .WithReference(configuration)
    .WithReference(redis)
    .DependsOn(redis);

var appNetwork = resources
    .AddNetwork("app")
    .WithDisplayName("App Network");

resources
    .AddService("service:example-web-api")
    .WithDisplayName("Example Web API")
    .Targets("application:example-web-api")
    .WithNetwork(appNetwork)
    .WithPort(
        "http",
        targetPort: 5127,
        port: 8080,
        protocol: "http",
        exposure: ResourceExposureScope.Public);
```

ASP.NET Core project declarations serialize their build step before launch and
then run without triggering another build:

```bash
dotnet build samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --nologo
dotnet run --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --no-build --no-launch-profile
```

Set `hotReload: true` when you want `dotnet watch` for a project resource.
CloudShell runs watch mode as non-interactive and asks `dotnet watch` to restart
on rude edits instead of prompting.

ASP.NET Core project endpoint sources are resolved in a fixed order:

1. Programmatic endpoints declared with `endpoint`, `WithEndpoint(...)`,
   `WithHttpEndpoint(...)`, `WithHttpsEndpoint(...)`, or
   `WithEndpointPort(...)`.
2. `Properties/launchSettings.json` only when the declaration explicitly calls
   `WithLaunchSettingsEndpoints()`.
3. The ASP.NET project provider default: a stable local HTTP endpoint.

Explicit endpoint declarations always win. If endpoints are declared manually,
CloudShell ignores launch settings even when launch-settings endpoint loading
was enabled earlier in the builder chain. CloudShell injects the resolved
endpoint into `ASPNETCORE_URLS`, so the project binds to the Resource Manager
endpoint without relying on launch profiles unless that launch-settings source
was explicitly opted into. Provider defaults are local development bindings, not
a general exposure mechanism; public or broader resource exposure should be
declared explicitly. Named endpoints match the Aspire URI shape
`https+http://_endpointName.serviceName`.

Endpoint helpers compile down to the networking primitives. A fixed endpoint
URI or fixed helper port becomes a manual endpoint mapping in the implied local
network. An endpoint helper without a fixed port becomes an explicit
auto-assigned mapping, allowing the local network/provider to choose the
concrete address while keeping the resource-owned service port stable.

`AddDocker()` declares the default local Docker host resource. The Docker
resource can specify a registry with `WithRegistry(...)`; the registry defaults
to Docker Hub (`docker.io`) and declared child containers inherit it. Add
`WithRegistryCredentialsFromEnvironment(username, passwordEnvironmentVariable)`
when the registry requires authentication. Containers are declared from that
Docker resource with `AddContainer(id, image, tag)`, following the
Aspire-style logical resource ID shorthand. CloudShell derives the stable
container resource ID as a child of the Docker host resource and the container
identity, then declares the container as a sub-resource of the Docker resource
that created it.

Use `DependsOn(...)` to add topology dependencies without changing the
container's parent relationship to its Docker host. When you need a custom
container resource ID or display name, use
`AddDocker().AddDockerContainer(id, image).WithDisplayName(...)`. To model
more than one Docker parent, use `AddDocker(id).WithDisplayName(...)` and
add containers from the returned Docker resource builder:

```csharp
var devDocker = resources
    .AddDocker("dev")
    .WithDisplayName("Development Docker");
var testDocker = resources
    .AddDocker("test")
    .WithDisplayName("Test Docker")
    .WithRegistry("https://registry.example.com");

var devRedis = devDocker.AddContainer("redis-dev", "redis", "7.2");
var testRedis = testDocker.AddContainer("redis-test", "redis", "7.2");
```

When the application starts, CloudShell maps referenced resource endpoints into
the .NET configuration shape used for service discovery, which provides a level
of compatibility with Aspire applications:

```text
services__<resource-name-or-id>__<endpoint-name-or-scheme>__0=<endpoint-address>
```

See [Service discovery](service-discovery.md) for the current
`Microsoft.Extensions.ServiceDiscovery` package requirements and the line
between this application-level projection and future network-level discovery.

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
Declaration attributes follow the same conventions as provider-projected
attributes: dotted lower-camel names, string-only values, invariant formatting,
and no secrets. Prefer provider-specific builder methods when the value is
provider configuration rather than a projected inspection fact.
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

Startup auto-start is the declaration intent for local-dev scenarios where
programmatically declared resources should start when the Control Plane starts.
Configure the graph default with `resources.WithAutoStart(...)`:

```csharp
controlPlane.Resources(resources =>
{
    resources.WithAutoStart(true);

    resources
        .AddAspNetCoreProject(
        "api",
            "API",
            "src/Api/Api.csproj")
        .WithAutoStart(false);
});
```

The graph default is inherited by resources that do not set their own value.
Provider policies can also define defaults for their resource declarations.
Effective startup behavior is resolved as resource override, provider default,
then graph default. UI-created resources do not use this startup path; create
flows use an explicit start-after-create option whose initial value comes from
provider policy.

Dependency auto-start is a separate lifecycle policy. When a Start or Restart
action is executed with dependency startup enabled, CloudShell may start stopped
dependencies before starting the requested resource. Configure the graph default
with `resources.WithDependencyAutoStart(...)`:

```csharp
controlPlane.Resources(resources =>
{
    resources.WithDependencyAutoStart(false);

    var database = resources
        .AddContainer("postgres", "postgres:16-alpine")
        .WithDependencyAutoStart(true);

    resources
        .AddAspNetCoreProject(
        "api",
            "API",
            "src/Api/Api.csproj")
        .DependsOn(database);
});
```

The graph default is inherited by resources that do not set their own value.
Use `WithDependencyAutoStart(false)` on a resource when it should not be started
automatically as a dependency, such as an expensive local database or an
externally managed service. Explicitly running that resource still works. If a
resource depends on a stopped dependency whose effective auto-start setting is
disabled, CloudShell stops the action with a clear error instead of silently
starting that dependency.

Lifecycle execution is selected through `IResourceOrchestrator`. CloudShell
ships a default orchestrator that preserves the current provider-backed
behavior and treats service exposure as host-local. Extensions can add
orchestrators such as Docker Compose and translate the same resource graph,
network, service, and exposure descriptors into runtime-specific artifacts.

The Docker Compose orchestrator runs lifecycle commands through `docker compose`
for matching Compose service names. For example, `application:api` maps to the
`api` Compose service. `UseDocker(...)` registers Docker as the implicit
default container host and enables Docker Compose orchestration without adding
Docker to the resource graph. `UseContainerHost(...)` can register an explicit
configured host when the app should target a non-default host.

`AsContainer(...)` is the project-to-container hook for ASP.NET Core project
resources. It converts the projected resource to an `application.container-app`
while preserving project metadata in the workload descriptor. If no Dockerfile
is supplied, the default local runner uses the .NET SDK container publish path
(`dotnet publish /t:PublishContainer`) for the project before running the
resulting image. If the project owns a Dockerfile, pass it through
`AsContainer(dockerfile: "Dockerfile")` and the selected container host builds
that Dockerfile before running the image. Pass `tag: "..."` when the project
container image should use a predictable tag instead of the generated
container-app revision value.

Low-level project builders still expose `AsContainerImage(...)` and
`WithContainerBuild(...)` for advanced provider code that wants to set image or
build metadata directly. A resource declared through `AddContainer(...)` is
already a top-level container app. A plain local executable without container
image or build metadata remains a default-orchestrator workload.
Container-app builders expose `WithImage(...)` because image selection is part
of the container app domain. Provider-specific service builders should expose
domain settings instead. For example, a SQL Server builder should expose
validated SQL Server version/edition choices and let the provider map those to
known runtime images or non-container implementations behind the provider
boundary. The Resource Manager settings can record a preferred container host,
but application and service declarations do not need to be tied to a specific
runtime implementation in the user-facing graph.

The explicit Docker resource model remains available separately. Use
`AddDockerProvider()` plus `resources.AddDocker()` when Docker itself should
appear as a managed resource with child container resources. It can coexist with
an implicit default container host; CloudShell does not enforce that both point
at the same runtime instance.

Declaring a Docker container under a Docker host is a local-development desired
state statement. It means CloudShell expects that container to exist on that
host, or that the provider/orchestrator may create or start it when the
declaration and lifecycle policy allow it. Endpoint declarations on the
container describe the expected container port contract and any host-local
mapping before the runtime container has been created, so the Resource Manager
can display the resource and run preflight checks before start.

The active orchestrator can be changed from Resource Manager settings. Changing
orchestrators does not migrate existing runtime state; the selected orchestrator
is used for future lifecycle actions when it can handle the target resource, and
CloudShell falls back to the default orchestrator otherwise.

By default, programmatic resources are not persisted. The code declaration is
the source of truth for the current process. This is the normal starting point
for local distributed-application development: the combined host starts the
local Control Plane, projects the declared graph, and lets the developer
iterate without first deciding how the environment will be hosted. If the
declaration is removed from startup code, the resource no longer appears after
restart unless it was persisted separately.

Transient declarations are not serialized into the CloudShell database. The
Resource Manager may project them through the same resource graph as persisted
resources, but the declaration object and provider-owned configuration remain
in memory for that process unless the declaration explicitly calls
`Persist()`.

Use `Persist()` when the declaration should materialize into provider-owned
configuration and the core resource registration store using the same provider
setup logic as the UI:

```csharp
controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("shared")
        .WithDisplayName("Shared Configuration")
        .WithEntry("FeatureFlags:UseNewFlow", "true")
        .Persist();
});
```

Persisted declarations are written during startup after the CloudShell database
has been initialized. After that, the resource can continue to exist even if the
declaration is removed from code.

Persistence is the handoff point where the development flow changes. Before
that point, the checked-in programmatic graph is the authority and the local
Control Plane is mostly projecting developer intent. After persistence, the
Control Plane's resource state and provider-owned configuration become the
environment record. A developer can still run and iterate locally, but changes
now need to be considered as updates to a managed environment.

Deployment is deliberately separate from persistence. `Persist()` records the
resource graph and provider configuration in the Control Plane; it does not
deploy that graph to a target. An on-premise CloudShell environment is a
deployment target: a standalone CloudShell cloud environment, potentially for
shared hosting, similar in role to future targets such as Azure or AWS.
Deploying persisted resources to any target should use the orchestrator
deployment API once that API is ready. Whether that deployment is triggered
from a CLI, Resource Manager UI, or another automation surface is a later
product decision. Until then, persisted programmatic resources can establish
the intended environment state, while runtime deployment remains constrained to
the existing local/startup and provider lifecycle paths.

The intended flow is:

1. Start with programmatic declarations in a local combined host.
2. Persist the resources when the graph is ready to become Control Plane state.
3. Continue local development against the same model.
4. Deploy the persisted graph through the orchestrator deployment API when that
   separate mechanism exists for the target environment.

`Persist()` does not overwrite an existing persisted resource. Use
`Persist(overwrite: true)` when checked-in configuration should replace the
current persisted provider configuration and registration metadata:

```csharp
controlPlane.Resources(resources =>
{
    resources
        .AddConfigurationStore("shared")
        .WithDisplayName("Shared Configuration")
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
CloudShell does not create core registration rows for the declaration, and
provider-specific configuration is kept in memory for the current process.

In split deployments, keep this API in the Control Plane host. The UI host
should discover resources through the Control Plane API rather than declaring
resources itself.

Today, `Persist()` is an in-process declaration instruction applied by the
Control Plane host during startup. A remote deployment flow is a separate
orchestrator capability, not an extension of UI-host declaration logic.

See [Hosting model](hosting-model.md).
