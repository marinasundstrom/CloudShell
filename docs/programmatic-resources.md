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
    .AddDockerProvider();

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
- `AddExecutable(...)` and `AddExecutableApplication(...)` from
  `CloudShell.Providers.Applications`.
- `AddDocker(...)` from `CloudShell.Providers.Docker`.

## Declarative Resource Graph

Programmatic resources can also be used in an Aspire-like style for local
development. In this workflow, resource declarations return builder objects that
can be passed to executable applications. This keeps resource relationships
strongly connected in code instead of repeating string IDs at each call site.

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

Executable applications have one additional Aspire-compatible concept:
endpoint references. `WithReference(resource)` records that the application wants
endpoint/configuration values for another resource. It does not, by itself,
enable service discovery variables. `WithServiceDiscovery()` is the opt-in that
maps referenced resource endpoints into the .NET configuration shape. This keeps
CloudShell open to other service discovery mechanisms, such as a dedicated
service discovery service running in a container.

Executable applications also keep `WaitFor(resource)` as an Aspire-compatible
alias for dependency ordering. Prefer `DependsOn(resource)` when describing the
CloudShell resource graph.

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
    .AddExecutableApplication(
        "application:example-web-api",
        "Example Web API",
        executablePath: "dotnet",
        arguments: "run --project samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj --no-launch-profile",
        endpoint: "http://localhost:5127")
    .WithContainerImage("example-web-api:dev")
    .WithReference(configuration)
    .WithReference(redis)
    .DependsOn(redis)
    .WithServiceDiscovery();

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
var endpoint = builder.Configuration.GetResourceEndpoint(
    "configuration-example",
    "entries");

var managementEndpoint = builder.Configuration.GetResourceEndpoint(
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

The generic `ICloudShellResourceBuilder` still supports string IDs as a lower
level escape hatch, but typed builders should prefer resource-builder overloads
for dependencies, endpoint references, and provider-specific relationships.

The host sample declares only `Example Configuration` programmatically. Other
resources are expected to be added through the Resource Manager UI unless a host
chooses to declare more of them in code.

## Runtime Behavior

Programmatic declarations are registered when CloudShell starts. They appear in
Resource Manager, participate in authorization, can be assigned to a resource
group with `WithResourceGroup(...)`, and can declare dependencies with
`DependsOn(...)` or provider-specific dependency methods.

Lifecycle execution is selected through `IResourceOrchestrator`. CloudShell
ships a default orchestrator that preserves the current provider-backed
behavior and treats service exposure as host-local. Extensions can add
orchestrators such as Docker Compose and translate the same resource graph,
network, service, and exposure descriptors into runtime-specific artifacts.

The Docker Compose orchestrator extension runs lifecycle commands through
`docker compose` for matching Compose service names. For example,
`application:api` maps to the `api` Compose service. When an executable
application uses `WithContainerImage(...)` or `WithDockerBuild(...)`, it can be
materialized into generated Compose YAML. A plain local executable without
container image or build metadata remains a default-orchestrator workload.
Container engines are orchestrator targets, not logical parents for top-level
application resources. A Docker Engine or Podman-compatible endpoint can be
exposed as an infrastructure resource when operators need to manage it, but
application and service declarations do not need to be tied to a specific engine
in the user-facing graph.

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
