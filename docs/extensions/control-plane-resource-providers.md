# Control Plane Resource Providers

Control Plane resource providers are non-UI extensions. They project and
operate resources, expose provider-owned logs and templates, and implement the
resource-domain behavior consumed by the shell, API, remote clients, samples,
and programmatic declarations.

When a provider is user-facing, pair this surface with a
[Resource Manager UI extension](resource-manager-ui.md). The UI integration is
what lets users register, update, inspect, and operate the resources through
CloudShell Resource Manager.

Control Plane extensions can be valid without UI integrations when the host
does not use CloudShell UI or the feature is deliberately programmatic-only.
Still, every resource provider should make that call explicitly. For
interactive features, the Resource Manager UI integration is part of the
expected implementation chain.

## Internal Resource Providers

Implement `IResourceProvider` to contribute discovered resource data:

- resources
- lifecycle state
- resource-bound actions
- dependencies
- named endpoints

Providers are aggregated by Resource Manager. Other extensions consume
`IResourceManagerStore`; they do not depend on provider implementations.

Registering an internal provider gives the Control Plane resource behavior. It
does not, by itself, create the shell UI for registering, updating, inspecting,
or operating that resource type. Add the corresponding Resource Manager UI
contributions when a resource is meant to be managed by users. A provider can
intentionally be programmatic-only, but that should be a conscious product
decision rather than an accidental omission.

## Provider Runtime Adapter Boundary

Providers are part of the extension model, so their dependency direction should
point toward CloudShell abstractions and provider-owned adapter contracts, not
toward a concrete host or runtime implementation.

A Resource model provider owns the resource type semantics: accepted
attributes, validation, graph projection, actions, log declarations, and
operation descriptions. When the provider needs to start a process, run a
container, reconcile a network mapping, write local files, inspect a runtime
object, or drive an orchestrator operation, it should call a focused adapter
interface such as a runtime controller, inspector, reconciler, command runner,
or deployment handler. The host, sample, or default runtime package then
registers the concrete implementation for the current environment.

This keeps providers portable across hosting environments:

- provider packages register resource definitions, providers, validators,
  actions, and adapter-facing services;
- host/runtime packages register process, Docker, filesystem, networking,
  sidecar, and orchestration implementations for the adapters the installed
  providers can use;
- missing adapters should produce unavailable actions or diagnostics, not
  hidden direct coupling to a specific host;
- default implementations can remain near built-in providers while the
  migration is underway, but reusable host-shaped implementations should move
  behind a default runtime integration package.

## Programmatic Resource Factories

Programmatic resource factories are extension points too. Provider packages can
add `IResourceDeclarationBuilder` extension methods that return the shared
resource and workload builder contracts from `CloudShell.Abstractions`, such as
`IResourceBuilder`, `IExecutableResourceBuilder`, `IProjectResourceBuilder`, or
`IContainerResourceBuilder`.

The provider owns the implementation and provider-specific configuration, but
the public builder contract stays aligned with CloudShell's uniform `Resource`
projection instead of introducing runtime resource subclasses.

## Resource Projection

When providers project `Resource.Attributes`, follow the shared attribute
conventions:

- use dotted lower-camel names such as `workload.kind` or `acme.cluster`
- keep values non-secret and string-only
- format numbers with invariant culture and booleans as lower-case `true` or
  `false`
- use stable non-localized tokens for enum-like values
- keep provider configuration, structured payloads, and runtime-only state
  behind provider contracts

Use names from `ResourceAttributeNames` only for their documented CloudShell
meaning. Provider-specific attributes should use a stable provider or domain
prefix so generated details and diagnostics can display them without creating a
global naming collision.

## Resource Actions

Resources can expose actions through `Resource.ResourceActions`. These actions
belong to the resource instance, not only to the resource type, so providers
can vary commands by state or capability. Use the standard `ResourceActionKind`
values for Start, Stop, Pause, and Restart when a command controls lifecycle. Use
`ResourceActionKind.Custom` with a stable action ID for provider-specific
commands.

`ResourceAction` is the command contract. `ResourceActionPresentation` is UI
policy for that command, including whether the action should be shown inline or
in overflow, which icon to use, and whether the UI should ask before invoking
it. Providers still execute the action normally when `ExecuteActionAsync` is
called; confirmation is not part of provider execution.

Providers that support actions implement
`IResourceProcedureProvider.ExecuteActionAsync`. The Resource Manager passes
the selected `Resource` and `ResourceAction` back to the provider, letting the
provider execute the command against the underlying system.

```csharp
return new Resource(
    Id: "acme:worker:orders",
    Name: "orders",
    Kind: "Acme Worker",
    Provider: "Acme",
    Region: "local",
    State: ResourceState.Running,
    Endpoints: [],
    Version: "1.0.0",
    LastUpdated: DateTimeOffset.UtcNow,
    DependsOn: [],
    Actions:
    [
        ResourceAction.Stop,
        ResourceAction.Restart,
        new ResourceAction(
            "acme.drain",
            "Drain",
            Presentation: new ResourceActionPresentation(
                ResourceActionDisplayStyle.Overflow,
                ResourceActionIcon.Custom,
                RequiresConfirmation: true))
    ]);
```

## Log Providers

Logs are first-class services registered independently of resources. Use
`ResourceLogSource` declarations for logs owned by a resource model. Implement
`ILogSourceContributor` when an extension only needs to contribute projected
log source metadata for the Control Plane catalog. Implement `ILogProvider`
when the extension manages a specific log source type or source and can decide
whether it can open a resolved `LogSource` and materialize an
`ILogSourceSession`. Register contributors with
`AddLogSourceContributor<TContributor>()` and providers with
`AddLogProvider<TProvider>()`.

Resource providers should declare stable, provider-owned defaults on resources
with `ResourceLogSource`. A source declaration is primarily a discovery
contract: it tells the Control Plane which logs a resource produces or can
expose so the platform can provide controlled access, persistence, query, and
streaming services around them. The declaration records the kind, format,
storage, capabilities, availability, origin, purpose, configuration metadata,
location, and physical producer. The Control Plane projects those declarations
plus provider-owned source projections into `LogSource` records. A single
resource can have multiple log sources, and
multiple providers can expose log sources for the same resource. Resources that
expose or allow configuration of log sources should advertise
`ResourceCapabilityIds.LogSources`; future UI configuration should be driven by
that capability and the source configuration metadata. Resource Manager,
Observability, provider pages, and the Logs view use projected `LogSource`
records for listing, counts, and navigation.

Providers that own log access should return `LogSource` records from
`ILogProvider.GetLogSources()` for provider-owned or runtime-discovered
sources that are not naturally declared on a resource. The Logs view opens
resource-scoped log lists through `/logs?resourceId=...`.

Declare `LogSourceCapabilities.Stream` only when the provider can support live
tail semantics. Streaming-capable sources are tailed automatically in the Logs
view when selected, and users can pause or resume streaming from the log
header. The viewer keeps a bounded entry window: it loads the newest page
first, appends streamed entries into that window, and fetches older pages only
when requested. It follows the latest entry only while the user is already at
the latest content; if they scroll back, new entries continue to append without
moving their position.

```csharp
public sealed class AcmeLogProvider : ILogProvider
{
    public string Id => "acme";

    public string DisplayName => "Acme";

    public IReadOnlyList<LogSource> GetLogSources() =>
    [
        new LogSource(
            Id: "acme:worker:orders:stdout",
            Name: "stdout",
            Provider: DisplayName,
            SourceName: "orders",
            SourceKind: LogSourceKind.Resource,
            ResourceId: "acme:worker:orders",
            Kind: ResourceLogSourceKind.ProcessStdout,
            Format: LogFormat.JsonConsole,
            Capabilities: LogSourceCapabilities.Read |
                LogSourceCapabilities.Stream |
                LogSourceCapabilities.StructuredFields)
    ];

    public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        // Read one bounded page. Use before to page back from the oldest visible entry.
        return Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

    public async IAsyncEnumerable<LogEntry> StreamLogSourceAsync(
        string logSourceId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in await ReadLogSourceAsync(logSourceId, initialEntries, cancellationToken: cancellationToken))
        {
            yield return entry;
        }

        await foreach (var entry in TailBackingSystemAsync(logSourceId, cancellationToken))
        {
            yield return entry;
        }
    }
}
```

When projecting a resource, include a default declaration for the same source:

```csharp
new Resource(
    Id: "acme:worker:orders",
    Name: "orders",
    Kind: "Worker",
    Provider: "Acme",
    Region: "local",
    State: ResourceState.Running,
    Endpoints: [],
    Version: "1.0",
    LastUpdated: DateTimeOffset.UtcNow,
    DependsOn: [],
    Capabilities: [new(ResourceCapabilityIds.LogSources)],
    LogSources:
    [
        new ResourceLogSource(
            Id: "stdout",
            Name: "stdout",
            Kind: ResourceLogSourceKind.ProcessStdout,
            Format: LogFormat.JsonConsole,
            Capabilities: LogSourceCapabilities.Read |
                LogSourceCapabilities.Stream |
                LogSourceCapabilities.StructuredFields,
            Origin: ResourceLogSourceOrigin.ProviderDefault,
            Purpose: ResourceLogSourcePurpose.Default,
            Availability: LogSourceAvailability.ResourceRunning)
    ]);
```

Streaming implementation guidance:

- Keep `ReadLogSourceAsync` as the bounded snapshot API. It should return recent
  entries and complete quickly. When `before` is supplied, return the newest
  entries older than that timestamp so the view can page backward.
- Override `StreamLogSourceAsync` only for sources that advertise
  `LogSourceCapabilities.Stream`.
- Honor cancellation promptly. The Logs view cancels the stream when users
  pause streaming, select another log, or leave the page.
- Yield parsed `LogEntry` values as complete lines/events arrive. Do not
  buffer indefinitely.
- Use `initialEntries` to optionally replay recent context before live events.
  Pass `0` through to the backing system when the caller wants only new
  entries.
- The control-plane endpoint
  `GET /api/cloudshell/log-sources/{logSourceId}/stream?initialEntries=50` streams
  newline-delimited JSON (`application/x-ndjson`) for API clients.
- The snapshot endpoint
  `GET /api/cloudshell/log-sources/{logSourceId}/entries?maxEntries=100&before=...`
  returns one bounded history page. Use it to load older entries incrementally
  instead of loading complete logs.

## Docker Built-in Provider

The Docker provider is the reference implementation. Container log sources
set `SupportsStreaming: true`; `ReadLogSourceAsync` calls Docker logs with
`Follow = false` and a bounded `Tail`, while `StreamLogSourceAsync` optionally
replays recent entries and then calls Docker logs with `Follow = true` and
`Tail = "0"`. Docker stdout and stderr frames are read incrementally with
`MultiplexedStream.ReadOutputAsync`, converted into `LogEntry` values, and
yielded as each newline is completed.

The Docker reference extension uses this hierarchy:

```text
Local Docker Host
- detail route: /resources/container-hosts
- Docker Container sub-resources
  - depend on docker:engine
```

Docker discovery runs in a background service and publishes an in-memory
resource snapshot. Provider connectivity never blocks shell page rendering.

Docker container sub-resources expose actions from the resource API. Running
containers expose Stop, Pause, and Restart. Stopped containers expose Start.
Paused containers expose Resume, Stop, and Restart.

Docker can also be declared programmatically in the resource graph. The
declarative API models Docker as the parent resource and containers as
sub-resources:

```csharp
controlPlane.DefineResources(resources =>
{
    var docker = resources.Declarations.AddDocker("docker:dev")
        .WithDisplayName("Development Docker");

    docker
        .AddContainer("redis", "redis", "7.2")
        .DependsOn("configuration:settings");
});
```

`AddDocker()` declares the default local Docker host. `AddDocker(id, name)`
allows more than one Docker host parent to be modeled. A Docker resource can
specify a registry with `WithRegistry(...)`; the registry defaults to Docker
Hub (`docker.io`) and is projected as `container.registry`. Add
`WithRegistryCredentialsFromEnvironment(username, passwordEnvironmentVariable)`
when the registry requires authentication. Containers created from a Docker
builder are parented to that specific Docker resource and inherit its registry
and credential reference, while `DependsOn(...)` records normal resource graph
dependencies.

The Docker endpoint is discovered from:

1. An endpoint configured through `AddDockerProvider`.
2. The `DOCKER_HOST` environment variable.
3. Docker Desktop's user socket.
4. A rootless Docker runtime socket.
5. `/var/run/docker.sock`.

```csharp
var controlPlane = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            resources.Declarations.AddDocker("docker:dev")
                .WithDisplayName("Development Docker");
        });
    });

controlPlane.AddDockerProvider(options =>
{
    options.Endpoint = new Uri("unix:///var/run/docker.sock");
    options.RefreshInterval = TimeSpan.FromSeconds(15);
});
```
