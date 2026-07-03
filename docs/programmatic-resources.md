# Programmatic Resources

CloudShell resources can be declared in code as an alternative to the Add
Resource UI or a serialized `ResourceTemplate`. Declarations are a Control
Plane concern: install the resource types you need in the Control Plane host,
then declare `ResourceDefinition` entries through the Resource model builder.
This lets a host check in its baseline configuration instead of relying on
every developer or operator to add the same resources by hand.

For local development, the preferred authoring shape is now a launcher app that
builds a `ResourceTemplate` and applies it to a CloudShell host profile. The
launcher can start the local host profile or attach to an existing Control
Plane by URL and credentials. The host profile composes the Control Plane,
CloudShell UI, providers, and runtime adapters; the launcher remains a
ResourceTemplate authoring and bootstrap client.

Programmatic declarations are not intended to stay C#-only, and the C# hosting
surface should not become a special integration path that other languages must
copy outside the resource model. Treat C#, TypeScript/JavaScript, and future
SDKs as language bindings over the same hosting-integration pattern: fluent
builders produce ResourceDefinition-based graph shapes, a launcher or API call
starts or targets a CloudShell host, and the Control Plane accepts the graph.
The language SDK may provide TypeScript, JavaScript, Java, Python, or other
ecosystem-specific helpers, but the accepted resource graph, provider
validation, lifecycle behavior, persistence, and Resource Manager projection
remain Control Plane-owned. See the
[cross-language local development proposal](proposals/core/cross-language-local-development.md).

```csharp
using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication.CreateBuilder("sample", args);

app.DefineResources(resources =>
{
    resources
        .AddConfigurationStore("example")
        .WithDisplayName("Example Configuration")
        .WithEndpoint("http://localhost:5138");
});

return (await app.RunAsync(new()
{
    CliProjectPath = "../../CloudShell.Cli/CloudShell.Cli.csproj",
    HostProjectPath = "../../CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj",
    HostUrl = new Uri("http://127.0.0.1:5099"),
    ControlPlaneUrl = new Uri("http://127.0.0.1:5099")
})).ExitCode;
```

`CloudShell.AppHost.Launcher` is the preferred C# launcher authoring path for
new local-development samples when the application does not need to customize
CloudShell itself. It reuses `ResourceGraphBuilder`, writes YAML or JSON
templates, and owns the launcher flow that starts or targets a host and
applies the graph. The app project references provider builder packages for
the resources it declares; the launcher package itself does not reference
Control Plane services, UI hosting, or provider runtime services. Launcher
apps should use
`CloudShell.LocalDevelopmentHost` by default. It is the stable local host
profile that includes the built-in Control Plane, UI, Resource Manager,
provider presets, and local runtime adapters. Specialized scenarios can still
provide a custom `HostProjectPath`; those host profiles use
`AddCloudShellControlPlaneApplication(...)` to install the built-in Resource
Model provider preset and graph-backed Resource Manager integration. A host
that also wants CloudShell UI should add it separately with
`AddCloudShellUi(...)` and register Resource Manager UI extensions in that UI
callback. A split UI host installs UI integrations and remote client adapters
instead of provider runtime packages.
Built-in provider registration does not seed default environment resources.
Defaults are authored through lazy graph builder accessors or through graph
helpers that need them. When a helper needs a network and none was supplied, it
uses `GetDefaultNetwork()`. When a container-backed helper needs a host and none
was supplied, it uses `GetContainerHost()`. If no resource needs a default, the
default Host network or container host is not added to the graph.

Runtime adapters remain host choices, but the built-in provider preset installs
the common graph-backed Resource Model runtime adapters by default. This covers
local-development endpoint-mapping and DNS/name-mapping reconciliation through
the Control Plane runtime contracts. Specialized hosts can still compose
individual provider registration methods or call
`UseBuiltInResourceModelRuntimeAdapters()` explicitly when they do not use the
broad provider preset.

```csharp
cloudShell
    .UseBuiltInResourceModelRuntimeAdapters();
```

Individual provider registration methods remain available for specialized
hosts and future split provider packages.

Built-in Resource model providers expose specialized extension methods
for their own resource types. Current provider methods include:

- `AddConfigurationStore(...)` from the configuration-store built-in
  provider.
- `AddSecretsVault(...)` from the secrets-vault built-in provider.
- `AddExecutableApplication(...)` from the executable application built-in
  provider.
- `AddAspNetCoreProject(...)` from the ASP.NET Core project built-in
  provider.
- `AddContainerApplication(...)` from the container application built-in
  provider.
- `AddJavaApp(...)` from the Java app built-in provider.
- `AddDockerHost(...)` and Docker container declarations from the Docker host
  and container built-in providers.

The active authoring shape is a `ResourceTemplate` or builder-created
`ResourceDefinition` list. Provider packages own the extension methods and
builder implementations that translate fluent calls into uniform resource
definitions with provider-owned attributes and relationships.

For built-in application resources, those definitions describe the stable
resource and its runtime intent. Built-in providers handle projection and
operation semantics, while the host/runtime supplies adapter implementations
for local process, project, Docker, networking, configuration, secrets, and
orchestration behavior. See [Application resources](resources/application-resources.md).

The intended end state is a set of composable Resource model primitives: an
external provider declares its stable resource, chooses whether the runtime is
a local executable, project, container, or managed sub-resource set, and then
uses adapter contracts for common lifecycle, logs, telemetry, endpoint, health,
liveness, configuration, storage, and cleanup behavior. Provider authors should
only implement the parts that make their resource distinct.

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

CloudShell has a `ResourceGraphBuilder` for code-first authoring of resources
and resource templates. Builders emit `ResourceDefinition` values, not
Resource Manager declarations, so the same authored resource graph can be
serialized, imported, applied as a `ResourceTemplate`, or used by tests.
Provider builders are hand-authored next to the resource type provider they
target. Source generation remains a future option once the manual builders
show the stable conventions and customization points providers need.
The same issue applies to future language SDKs: provider-specific builder
wrappers and extension methods will need to exist in TypeScript/JavaScript and
other ecosystems if those SDKs are to feel as natural as C#. Hand-authored
wrappers are acceptable for early POCs, but provider metadata should eventually
be rich enough to generate the common builder shape and reduce drift between
language packages.
See [Launchers](launchers-and-app-hosts.md) for the terminology split between
host profiles, language-specific launchers, and runtime service clients.
Language launcher packages live under `Launchers/` so C#, TypeScript, Java,
and future launcher authoring surfaces are easy to find separately from runtime
service clients under `sdk/`. The experimental TypeScript launcher package
under `Launchers/TypeScript/cloudshell` is the first proof point for this
shape. The experimental Java launcher package under
`Launchers/Java/cloudshell-launcher` carries the first Java-native builder
surface. Both emit ResourceTemplate JSON that the current CloudShell CLI can
apply for advanced automation, while intentionally limiting hand-authored
builders until generation and provider metadata requirements are clearer.
For Resource Model provider ports, creating the provider-owned manual builder
is part of the porting work unless the provider README explicitly records why
the builder is deferred. The builder is the first code-first authoring surface
for the provider's `ResourceDefinition` shape and is used by tests to keep
resource-template setup aligned with the provider-owned interchange contract.

```csharp
var graph = new ResourceGraphBuilder();

graph
    .AddNetwork("app")
    .WithDisplayName("App Network")
    .WithHostReadiness("logicalOnly");

var template = graph.BuildTemplate("local-app", environmentId: "local");
```

`BuildTemplate(...)` produces desired resource state. It does not produce an
orchestrator deployment. Resource Manager applies the resource template into the
Resource Model, providers validate and plan provider-owned changes, and
deployment planning then projects accepted resource state to orchestrator services,
replica groups, load-balancer bindings, and the running system.

The current manual builders cover generic networks, virtual networks, host
networking, Configuration Store, Secrets Vault, storage, CloudShell
storage-backed volumes, local volumes, SQL Server, SQL Database, generic
container hosts, Docker hosts, Docker containers, container applications,
executable applications, ASP.NET Core projects, JavaScript apps, Java apps,
identity provisioning, services, DNS zones, name mappings, load balancers, and
host configuration sources. They are useful for test setup as well as host
authoring because provider tests can compose realistic resource templates
without repeating raw attribute dictionaries, configuration payloads,
capability payloads, and typed `ResourceReference` values.

The SQL builders cover declared database configuration, typed server
dependencies, and volume mount capability setup. Container and Docker builders
cover host dependencies, endpoint requests, replicas, image settings, registry
and container metadata, and volume mount capability setup. Executable,
ASP.NET Core project, JavaScript, and Java app builders cover command/project
settings, endpoint requests, environment variables, service-discovery
references, volume mounts, and health-check payloads. Identity provisioning
builders cover provider identity and provider-kind attributes used by the
runtime setup seam. Exposure builders cover service target/network
dependencies, DNS zone declaration, name mapping DNS-zone/target
dependencies, load balancer host/backend dependencies, and host configuration
source declaration. Configuration and secrets builders can declare service
endpoints and participate in dependencies, but they intentionally do not
author configuration entries or secret values as graph attributes. Those
values remain provider/runtime data.

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
variables, so `WithEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "...")`
can override provider defaults for a single resource.

```csharp
resources
    .AddAspNetCoreProject(
        "api",
        "src/API/API.csproj")
    .WithRuntimeMonitoring()
    .WithEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317");

resources
    .AddContainerApplication("worker")
    .WithImage("example/worker:dev")
    .WithRuntimeMonitoring();
```

Applications can depend on any declared resource builder, including sub-resources
such as containers returned from `resources.AddDocker().AddContainer(...)`.

CloudShell also includes an implicit Host network resource, explicit logical
and virtual network resources, and optional service resources. Programmatic
endpoint helpers can keep local development Aspire-compatible by declaring an
application endpoint and producing an endpoint mapping to the default Host
network. In the current local development topology, that mapping resolves to a
local address such as `localhost:<port>` or `127.0.0.1:<port>`. Resources may
default to the Host network when policy allows host-local bindings, but that
helper behavior is not the canonical networking model.

Default environment resources should be accessed through named builder entry
points when the template wants to make them explicit. In the ResourceDefinition
builder, `resources.GetDefaultNetwork()` returns the Host network resource and
`resources.GetContainerHost()` returns the default docker-compatible container
host resource. The accessors are get-or-add helpers, so repeated calls refer to
the same resource builder. Control Plane `DefineResources(...)` and
`DefineInitialTemplate(...)` callbacks receive a Control Plane
resource-definition context: it behaves like the ResourceDefinition graph
builder for resource declarations, while also exposing host-level services that
can contribute to graph construction. Identity providers are registered on the
host itself, for example by built-in identity host setup, `ResourceIdentity`
configuration, or host-level `controlPlane.AddIdentityProvider(...)` calls.
`resources.GetIdentityProvider()` reads that host context and returns an
identity-provider context that can create provider-scoped principal references,
for example `resources.GetIdentityProvider().GetUser("alice")`. Identity
providers are not emitted as `ResourceDefinition` entries.
For host placement, descriptors, capabilities, and resolver diagnostics, see
[Container Hosts](resources/container-hosts.md).

Graph-backed generic container-host resources project orchestration descriptors
for the runtime host resolver. This means a resource such as SQL Server can omit
an explicit host and let the SQL Server builder call `GetContainerHost()` while
building the graph. The runtime resolver can then select that authored default
host without a separate `UseDocker()` host registration path. Explicit
`UseContainerHost(...)` calls still win.

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
controlPlane.DefineResources(resources =>
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

For the shared storage and volume authoring model, see
[Storage and Volumes](resources/storage-and-volumes.md).

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
controlPlane.DefineResources(resources =>
{
    resources
        .AddContainerApplication("redis")
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
URI or fixed helper port becomes a manual endpoint mapping in the Host network.
An endpoint helper without a fixed port becomes an explicit auto-assigned
mapping, allowing the selected network/provider to choose the
concrete address while keeping the resource-owned service port stable.

`AddDocker()` declares the default local docker-compatible container host
resource. A configured default container host from `UseDocker(...)` is also
treated as an implicit container-host resource in the realized runtime model;
authored resources may default to it when the selected provider and environment
policy allow it. The Docker resource can specify a registry with
`WithRegistry(...)`; the registry defaults to Docker Hub (`docker.io`) and
declared child containers inherit it. Add
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
`resources.AddResourceGroup(...)` returns a group definition object that can be
passed to `WithResourceGroup(group)`; the string id overload remains available
for cases where only an id is known. Use `resources.Declare(...)` inside
`DefineResources(...)` for manual provider-backed resource declarations that do
not yet have a typed ResourceDefinition builder.

Startup auto-start is the declaration intent for local-dev scenarios where
programmatically declared resources should start when the Control Plane starts.
Configure startup on individual resource builders with `WithAutoStart(...)`:

```csharp
controlPlane.DefineResources(resources =>
{
    resources
        .AddAspNetCoreProject(
            "api",
            "src/Api/Api.csproj")
        .WithAutoStart(false);
});
```

Provider policies can also define defaults for their resource declarations.
UI-created resources do not use this startup path; create flows use an explicit
start-after-create option whose initial value comes from provider policy.

Dependency auto-start is a separate lifecycle policy. When a Start or Restart
action is executed with dependency startup enabled, CloudShell may start stopped
dependencies before starting the requested resource. Configure dependency
startup on individual resource builders with `WithDependencyAutoStart(...)`:

```csharp
controlPlane.DefineResources(resources =>
{
    var database = resources
        .AddContainerApplication("postgres")
        .WithImage("postgres:16-alpine")
        .WithDependencyAutoStart(true);

    resources
        .AddAspNetCoreProject(
            "api",
            "src/Api/Api.csproj")
        .DependsOn(database);
});
```

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
default container host and enables Docker Compose orchestration without
requiring a user-authored Docker host definition. The runtime may still project
that configured default as an implicit container-host resource in the realized
environment model. `UseContainerHost(...)` can register an explicit configured
host when the app should target a non-default host.

`AsContainer(...)` is the project-to-container hook for app resources. It
converts the projected resource to an `application.container-app` while
preserving project metadata in the workload descriptor. The provider for the
authored app owns the packaging strategy. ASP.NET Core can use the .NET SDK
container publish path (`dotnet publish /t:PublishContainer`) when no
Dockerfile is supplied, or a project Dockerfile when one is specified.
JavaScript apps use their project directory as the build context and should
provide a Dockerfile until framework-specific packaging support exists. Pass
`tag: "..."` when the project container image should use a predictable tag
instead of the generated container-app revision value.

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
`AddDockerProvider()` plus `resources.AddDocker()` when the Docker host should
be authored in the resource template and manage child container resources. It
can coexist with an implicit default container host; CloudShell does not enforce
that both point at the same runtime instance.

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
controlPlane.DefineResources(resources =>
{
    resources
        .AddConfigurationStore("shared")
        .WithDisplayName("Shared Configuration")
        .WithEndpoint("http://localhost:5138")
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
4. Apply the persisted graph as a resource template when Resource Manager
   should converge a target environment from checked-in resource intent.

`Persist()` does not overwrite an existing persisted resource. Use
`Persist(overwrite: true)` when checked-in configuration should replace the
current persisted provider configuration and registration metadata:

```csharp
controlPlane.DefineResources(resources =>
{
    resources
        .AddConfigurationStore("shared")
        .WithDisplayName("Shared Configuration")
        .WithEndpoint("http://localhost:5138")
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
