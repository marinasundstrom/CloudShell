# Control Plane and Resource Manager Extensions

Control Plane and Resource Manager extensions add resource-domain behavior and
the Resource Manager UI required to manage those resources. This includes
resource providers, resource creation, procedures, logs, templates,
programmatic declarations, Add Resource forms, update components, resource
tabs, detail routes, and resource UI actions.

These surfaces are related, but separate. A Control Plane provider registers
services that project and operate resources. A Resource Manager UI integration
registers the shell experience for those resources. User-facing providers
usually need both.

Generic shell views and navigation are covered by [UI extensions](ui.md).

## Resource Types

Resource types are the user-facing contract for adding resources. An extension
registers each type with `AddResourceType<TRegistrationComponent>`.

Resource type and UI registration are separate from Control Plane resource
provider registration. CloudShell UI and the Control Plane are distinct apps,
even when a development host runs both inside one ASP.NET Core process. A
Control Plane provider package can register services that project and operate
resources, while the UI extension registers the Resource Manager experience for
those resources. Most user-facing providers should ship both surfaces
together: programmatic declarations and Control Plane providers for
automation, plus resource type registration components, update components,
tabs, detail routes, and UI actions for shell users.

The registration component is rendered inside the common Add Resource page
after the user chooses the type from a dropdown. It owns the type-specific
form, validation, discovery hints, and the call to
`IResourceRegistrationStore.RegisterAsync`.

Resource providers are not shown as a product concept in the UI. They are
implementation services that resource types use to map external systems into
CloudShell resources.

Programmatic resource factories are extension points too. Provider packages can
add `IResourceDeclarationBuilder` extension methods that return the shared
resource and workload builder contracts from `CloudShell.Abstractions`, such as
`IResourceBuilder`, `IExecutableResourceBuilder`, `IProjectResourceBuilder`, or
`IContainerResourceBuilder`. The provider owns the implementation and
provider-specific configuration, but the public builder contract stays aligned
with CloudShell's uniform `Resource` projection instead of introducing runtime
resource subclasses.

Resource types can also provide default health checks through
`ResourceTypeProbeOptions`. This gives the Add Resource UI the same
health-check enablement path available to programmatic declarations such as
`WithHttpHealthCheck(...)`, while keeping enablement on the resource instance
explicit.

```csharp
builder.AddResourceType<Pages.RegisterAcmeService>(
    "acme.service",
    "Acme service",
    "Register an Acme service endpoint.",
    "server",
    20,
    probeOptions: new ResourceTypeProbeOptions(
    [
        new ResourceHealthCheck(
            "/healthz",
            EndpointName: "http",
            Name: "health")
    ]));
```

When a selected resource type has default health checks, the common Add
Resource page shows an "Enable resource health checks" checkbox. The checkbox
only controls the resource being created. If it is cleared, no health checks
are written to that resource even though the type supports them.

Registration components can consume the selected instance settings through the
cascading `ResourceRegistrationProbeContext` and copy them into their
provider-specific definition:

```csharp
[CascadingParameter]
public ResourceRegistrationProbeContext? ProbeContext { get; set; }

var definition = new AcmeServiceDefinition(
    id,
    name,
    healthChecks: ProbeContext?.GetSelectedHealthChecks());
```

Default checks are expectations, not proof that every resource instance
implements the endpoint. ASP.NET Core projects are a good example: a type may
provide `/healthz` or `/alive` defaults, but the app must map those endpoints.
If it does not, the Resources UI will correctly show an unhealthy or unknown
result unless the user disables the checks for that resource.

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
or operating that resource type. Add the corresponding UI contributions when a
resource is meant to be managed by users through Resource Manager. A provider
can intentionally be programmatic-only, but that should be a conscious product
decision rather than an accidental omission.

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

## Resource Manager Projection

The shell generates a default resource detail view from the projected
`Resource` when a provider does not contribute a specialized view. The
generated view shows stable identity, class, endpoints, attributes,
dependencies, health checks, actions, and observability details. The built-in
route is `/resources/{resourceId}/details?tab=overview`; provider-contributed
tabs use the same route with their tab ID in the `tab` query parameter.

Resources can set `DetailRoute` to link to an extension-owned view. This
supports the familiar cloud-portal pattern where a resource opens its own
operational workspace.

Resource types can also contribute tabs or an update component. Those
provider-owned views override the generated default for resources of that type.

## Resource Actions and UI Actions

Resources can expose actions through `Resource.ResourceActions`. These actions
belong to the resource instance, not only to the resource type, so providers
can vary commands by state or capability. Use the standard `ResourceActionKind`
values for Run, Stop, Pause, and Restart when a command controls lifecycle. Use
`ResourceActionKind.Custom` with a stable action ID for provider-specific
commands.

`ResourceAction` is the command contract. `ResourceActionPresentation` is the
UI policy for that command, including whether the action should be shown inline
or in overflow, which icon to use, and whether the UI should ask before
invoking it. Providers still execute the action normally when
`ExecuteActionAsync` is called; confirmation is not part of provider execution.

Providers that support actions implement
`IResourceProcedureProvider.ExecuteActionAsync`. The Resource Manager passes
the selected `Resource` and `ResourceAction` back to the provider, letting the
provider execute the command against the underlying system.

A UI action is different from a resource action. UI actions are custom Resource
Manager behaviors attached by a UI extension, such as opening a wizard,
navigating to a provider view, or invoking a resource action with additional
presentation state. Resource actions are domain operations in the resource
model and can be guarded by resource operation permissions. Resource Manager
may display standard lifecycle resource actions automatically. Custom UI
actions must be registered by the UI resource provider or extension that owns
the shell presentation.

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

Root resources are persisted registrations. Discovered resources stay hidden
until the user explicitly adds one through a resource type registration UI.
Descendants of a registered root can appear dynamically as sub-resources.

Resource groups are user-managed project boundaries owned by the platform, not
by providers. A root resource can be assigned to a group during registration,
and its sub-resources inherit that group for filtering and display.

Parent-child resource relationships are distinct from dependency
relationships: the parent controls containment in Resource Manager, while
`DependsOn(...)` records topology or ordering between any two resources.

## Log Providers

Logs are first-class services registered independently of resources. Implement
`ILogProvider` when an extension can expose logs for resources, providers, or
extension-owned artifacts, then register it with `AddLogProvider<TProvider>()`.

Each provider returns `LogDescriptor` values. A descriptor can point at a
resource through `ResourceId`, an extension artifact through `ArtifactId`, or a
provider-owned source through `SourceKind`. A single resource can have multiple
logs, and multiple providers can expose logs for the same resource. The
Resource Manager shows a log shortcut for resources with matching descriptors,
and the Logs view opens resource-scoped log lists through
`/logs?resourceId=...`.

Use `SupportsStreaming = true` only when the provider can support live tail
semantics. Streaming-capable logs are tailed automatically in the Logs view
when selected, and users can pause or resume streaming from the log header. The
viewer keeps a bounded entry window: it loads the newest page first, appends
streamed entries into that window, and fetches older pages only when requested.
It follows the latest entry only while the user is already at the latest
content; if they scroll back, new entries continue to append without moving
their position.

```csharp
public sealed class AcmeLogProvider : ILogProvider
{
    public string Id => "acme";

    public string DisplayName => "Acme";

    public IReadOnlyList<LogDescriptor> GetLogs() =>
    [
        new LogDescriptor(
            Id: "acme:worker:orders:stdout",
            Name: "stdout",
            Provider: DisplayName,
            SourceName: "orders",
            SourceKind: LogSourceKind.Resource,
            ResourceId: "acme:worker:orders",
            SupportsStreaming: true)
    ];

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        // Read one bounded page. Use before to page back from the oldest visible entry.
        return Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in await ReadLogAsync(logId, initialEntries, cancellationToken: cancellationToken))
        {
            yield return entry;
        }

        await foreach (var entry in TailBackingSystemAsync(logId, cancellationToken))
        {
            yield return entry;
        }
    }
}
```

Streaming implementation guidance:

- Keep `ReadLogAsync` as the bounded snapshot API. It should return recent
  entries and complete quickly. When `before` is supplied, return the newest
  entries older than that timestamp so the view can page backward.
- Override `StreamLogAsync` only for descriptors that set
  `SupportsStreaming = true`.
- Honor cancellation promptly. The Logs view cancels the stream when users
  pause streaming, select another log, or leave the page.
- Yield parsed `LogEntry` values as complete lines/events arrive. Do not
  buffer indefinitely.
- Use `initialEntries` to optionally replay recent context before live events.
  Pass `0` through to the backing system when the caller wants only new
  entries.
- The control-plane endpoint
  `GET /api/cloudshell/logs/{logId}/stream?initialEntries=50` streams
  newline-delimited JSON (`application/x-ndjson`) for API clients.
- The snapshot endpoint
  `GET /api/cloudshell/logs/{logId}/entries?maxEntries=100&before=...`
  returns one bounded history page. Use it to load older entries incrementally
  instead of loading complete logs.

## Docker Reference Provider

The Docker provider is the reference implementation. Container log descriptors
set `SupportsStreaming: true`; `ReadLogAsync` calls Docker logs with
`Follow = false` and a bounded `Tail`, while `StreamLogAsync` optionally
replays recent entries and then calls Docker logs with `Follow = true` and
`Tail = "0"`. Docker stdout and stderr frames are read incrementally with
`MultiplexedStream.ReadOutputAsync`, converted into `LogEntry` values, and
yielded as each newline is completed.

The Docker reference extension uses this hierarchy:

```text
Local Docker Host
├── detail route: /resources/container-hosts
└── Docker Container sub-resources
    └── depend on docker:engine
```

Docker discovery runs in a background service and publishes an in-memory
resource snapshot. Provider connectivity never blocks shell page rendering.

Docker container sub-resources expose actions from the resource API. Running
containers expose Stop, Pause, and Restart. Stopped containers expose Run.
Paused containers expose Resume, Stop, and Restart.

Docker can also be declared programmatically in the resource graph. The
declarative API models Docker as the parent resource and containers as
sub-resources:

```csharp
controlPlane.Resources(resources =>
{
    var docker = resources.AddDocker("docker:dev", "Development Docker");

    var redis = docker
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
builder
    .AddCloudShell()
    .AddDockerProvider(options =>
    {
        options.Endpoint = new Uri("unix:///var/run/docker.sock");
        options.RefreshInterval = TimeSpan.FromSeconds(15);
    });
```
