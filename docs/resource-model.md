# Resource Model

This document describes the low-level CloudShell resource object model: the
stable parts that make up a resource, what each part means, and how related
objects such as endpoints, mappings, actions, capabilities, ownership metadata,
and attributes fit together.

For broader product concepts, see [Domain model](domain-model.md).
For canonical product and domain vocabulary, see
[CloudShell Terminology](terminology.md).
For the Resource Graph POC interchange shape, see
[Resource Definition Structure](resource-definition-structure.md).

## Resource Projection

The low-level resource object is a projection returned by the Control Plane and
consumed by Resource Manager UI, remote clients, and extensions. It is not the
runtime object itself. It is the current known description of a managed
resource.

Resource projection is not the same thing as resource declaration. A declared
resource is the stable authored or persisted resource identity that CloudShell
expects to exist and that a resource provider validates and handles for its
resource type. A projected or listed resource is a resource-shaped artifact the
provider can surface from its runtime, platform, or child-resource inventory.
Both declared resources and projected/listed resources can be referenced inside
CloudShell, including by stable resource ID or by future weak references that a
provider can resolve and project on demand.

Today `IResourceProvider.GetResources()` is used for both declared resources
and provider-observed or runtime-managed artifacts, such as container replicas,
runtime containers, and provider-discovered child resources. That overloaded
surface is useful for building a unified graph, but it should not imply that
every projected artifact was explicitly declared by a user or program.
Projected/listed resources should initially be treated as read-only graph
members unless the provider exposes actions, update handling, orchestration
participation, or other management support for that projected resource. A
Docker container projected below a Docker/container host is a concrete example:
it is not a declared resource persisted by Resource Manager, and it does not
currently participate in orchestration, but the Docker provider can still
expose operations such as start and stop for it.

Resource Manager marks resources that pass through the normal graph projection
with `resource.graph.membership`: `declared` for resources that have been
declared, registered, imported, or otherwise accepted into stable inventory,
and `projected` for provider-listed artifacts included because they are related
to declared resources. This marker is diagnostic metadata. It does not change
whether a resource can be referenced or operated; those capabilities still
come from provider support, authorization, and resource actions.

Future Resource Manager refactoring should keep those concerns distinct:

- declared resource inventory: stable resources that have been authored,
  persisted, imported, or otherwise accepted into CloudShell as resources
- resource definitions: declarations of resource identity, resource type,
  optional definition version, typed payload, and provider-owned attributes
  that describe what CloudShell should accept as the resource's intended shape
- provider projection/listing: the current resource-shaped view of declared
  resources and provider-observed artifacts
- runtime/diagnostic projections: implementation artifacts that are useful for
  inspection, cleanup, health, logs, or topology but are not the primary
  declared resource
- references: strong or weak references to either declared resources or
  provider-projected resources that may be projected on demand by the owning
  provider
- validation and change application: provider-owned behavior for a declared
  resource type and its accepted attributes/capabilities

A resource definition can be projected into a serialized format such as JSON,
but the structure and the serialized format are separate concerns. Conceptually
a resource definition contains a resource name, a resource type, and the
resource-specific intent that the owning provider can validate. That intent may
be represented by a typed definition payload and by provider-owned attributes,
including complex typed values when the resource schema allows them. A
serialized projection might look like:

```jsonc
{
  "name": "acme-api",
  "type": "application.executable",
  "executable.path": "whatsup.exe",
  "executable.arguments": "doc",
  "executable.workingDirectory": ".",
  "custom.data": {
    "example": "complex value"
  }
}
```

The provider for `application.executable` owns validation and application of
those attributes. Resource Manager should not hard-code executable-specific
attribute semantics, but it can store, compare, version, reference, and project
resource definitions once the owning provider accepts them.

Complex attribute values are represented by the Resource model value tree, not
by a JSON-specific DOM. JSON, YAML, XML, database rows, and compact persistence
records are serializer or store projections over the same value model.
Consumers that implement provider/runtime behavior should be able to project a
complex value into a concrete CLR type, work with that type, and map it back to
the model value before validation, serialization, or persistence.

A resource projection is made from these groups:

| Group | Purpose |
| --- | --- |
| Identity | Canonical ID, scoped name, optional display name, type, class, provider, region, and version. |
| Lifecycle | Optional lifecycle state, last-updated time, supported actions, and health checks. |
| Relationships | Dependencies, parent, owner, source, management mode, visibility, and cleanup behavior. |
| Networking | Resource endpoint projections, endpoint network mappings, configured endpoint mappings, and load-balancer routes. |
| Capabilities | Role declarations used by the Control Plane, UI, and providers to select compatible resources. |
| Attributes | Stable, non-secret projected facts about the resource. |
| Observability | Resource log, event, trace, and metric metadata. |
| Identity binding | Optional resource identity metadata and non-secret identity projection. |
| UI routing | Optional detail route and provider/resource UI contribution metadata. |

The projection should stay passive. It can expose lookup and normalization
helpers, but operations still go through `IResourceManager` or the Control Plane
API.

## Resource Shape

The current implementation type is
`CloudShell.Abstractions.ResourceManager.Resource`. Its constructor is an
implementation detail, but the fields it projects are the low-level resource
contract:

| Field | Group | Meaning |
| --- | --- | --- |
| `Id` | Identity | Canonical resource identity. Used by API routes, permissions, dependencies, events, logs, provider state, and automation. |
| `Name` | Identity | Scoped resource name. This is what users and declarations normally provide. |
| `DisplayName` | Identity | Optional presentation label. It must not affect addressing or provider state. |
| `Kind` | Identity | Legacy/provider kind string. `EffectiveTypeId` falls back to this when `TypeId` is not set. |
| `TypeId` | Identity | Precise resource type/kind, such as `application.container-app`, `cloudshell.network`, or `secrets.vault`. |
| `ResourceClass` | Identity | Broad class used for filtering, common validation, and shared UI. |
| `Provider` | Identity | Provider that projected or owns the resource behavior. |
| `Region` | Identity | Region or locality label. Local resources commonly use `local`. |
| `Version` | Identity | Provider or projection version string. |
| `LastUpdated` | Lifecycle | Last time the projection changed or was observed. |
| `State` | Lifecycle | Optional lifecycle state. `null` means not applicable. |
| `Actions` | Lifecycle | Resource actions the Control Plane can execute for the resource. |
| `HealthChecks` | Lifecycle | Provider-projected health signals. |
| `DependsOn` | Relationships | Resource IDs this resource depends on for startup, operation, configuration, identity, networking, or diagnostics. |
| `ParentResourceId` | Relationships | Containment/context relationship. |
| `OwnerResourceId` | Relationships | Stable owner for provider-managed or runtime-managed artifacts. |
| `Source` | Relationships | Origin of the resource projection. |
| `ManagementMode` | Relationships | Who is expected to manage the resource. |
| `Visibility` | Relationships | Default visibility in the resource graph. |
| `CleanupBehavior` | Relationships | What should happen to this resource when the owner is removed. |
| `Endpoints` | Networking | Resource endpoint projections. |
| `EndpointNetworkMappings` | Networking | Current topology-specific reachable addresses for this resource's endpoints. |
| `EndpointMappings` | Networking | Configured source-to-target endpoint mappings, usually projected on network resources. |
| `LoadBalancerRoutes` | Networking | Routes projected by load-balancer resources. |
| `Capabilities` | Capabilities | Role declarations such as endpoint provider, volume provider, or name publisher. |
| `Attributes` | Attributes | Stable, non-secret projected facts. |
| `Observability` | Observability | Log, trace, metric, and resource event metadata. |
| `Identity` | Identity binding | Optional resource identity binding metadata. |
| `DetailRoute` | UI routing | Optional route to a custom resource detail view. |

## Identity Fields

`Id`, `Name`, and `DisplayName` are intentionally separate:

- `Id` is canonical identity and log/addressing identity inside CloudShell.
- `Name` is the stable scoped name users and programmatic declarations provide.
- `DisplayName` is optional presentation text and should be treated as a local
  development affordance.

When `TypeId` is missing, `Kind` is used as the effective type ID. New resource
types should set `TypeId` explicitly and keep `Kind` only for compatibility with
older projections.

## Resource Class

`ResourceClass` is a broad classification of the projected domain shape.

```csharp
public enum ResourceClass
{
    Generic,
    Executable,
    Project,
    Container,
    Service,
    Network,
    Storage,
    Configuration,
    Infrastructure,
    SecretsVault
}
```

Use `ResourceClass` for broad filtering, generated details, class-level
validation, and common UI grouping. Use `EffectiveTypeId`, capabilities, and
provider contracts for precise behavior.

Known resource types should declare their expected `ResourceClass`. The Control
Plane should reject invalid create metadata and normalize invalid provider
projections back to the known class while producing diagnostics.

## Lifecycle State

`State` is nullable:

```csharp
public enum ResourceState
{
    Running,
    Starting,
    Stopping,
    Paused,
    Degraded,
    Stopped,
    Unknown
}
```

`null` means the resource does not produce lifecycle state. This is appropriate
for logical resources such as DNS zones or name mappings.

`Unknown` means the resource participates in lifecycle state, but the provider
cannot currently determine it.

Materialization status is not lifecycle state. DNS publication, endpoint
mapping provisioning, identity provisioning, volume mount materialization,
deployment status, and provider setup readiness should use typed projections,
health checks, attributes, diagnostics, or resource events.

## Relationships

| Field | Meaning |
| --- | --- |
| `DependsOn` | Resource IDs this resource depends on for startup, operation, configuration, identity, networking, or diagnostics. |
| `ParentResourceId` | Containment/context relationship. Used for child resources such as projected containers or storage-owned volumes. |
| `OwnerResourceId` | Stable owner for provider-managed or runtime-managed artifacts. |
| `CleanupBehavior` | What should happen to this resource when the owner is removed. |

Ownership metadata:

```csharp
public enum ResourceSource
{
    User,
    Provider,
    Orchestrator,
    RuntimeController
}

public enum ResourceManagementMode
{
    UserManaged,
    ProviderManaged,
    OrchestratorManaged,
    RuntimeManaged
}

public enum ResourceVisibility
{
    Normal,
    Hidden,
    Diagnostic
}

public enum ResourceCleanupBehavior
{
    None,
    DeleteWithOwner,
    DetachWithOwner
}
```

Rules:

- Parent is not dependency.
- Owner is not necessarily parent.
- Hidden/runtime-managed resources still belong to the unified resource graph.
- Internal implementation artifacts should normally be hidden or diagnostic.
- Resource visibility is provider/default graph behavior, not caller
  authorization. Caller authorization is represented by effective resource
  access levels.

Future relationship work should distinguish strong resource relationships from
weak projected-resource references. `DependsOn` should remain a strong
relationship to a resource that exists in the current graph and can participate
in dependency warnings, startup ordering, authorization checks, and graph
inspection. A weak projected-resource reference would describe intent to point
at a provider-projected target that may not exist yet, such as a database under
a SQL Server resource or a runtime container under a container host. That
reference should carry enough owner/provider context, target type, and target
name or key for persistence and validation, but it should not imply that the
target resource has already been materialized. The provider that owns the
projection decides whether the reference is allowed before materialization,
whether it can be resolved later, and which diagnostics should be shown when
resolution fails.

Resource Manager selector components should follow the same distinction. A
standard selector can query existing projected resources by class, type,
provider, capability, group, or access level. Specialized hierarchical
selectors, such as SQL Server then database or Docker host then runtime
container, may need to return either a strong reference to an existing
projected resource or a weak projected-resource reference that the owning
provider validates and resolves later.

Resource Manager includes a resource dependency graph page that visualizes the
currently visible resource projections and their `DependsOn` relationships.
The graph is a UI projection over existing resource data: it filters by the
same Resource Manager display settings as the inventory page and does not add
new dependency semantics, lifecycle policy, or provider behavior.

## Resource Access

Resource access is the caller-specific authorization result for a resource.
It is ordered from no knowledge to full management:

```csharp
public enum ResourceAccessLevel
{
    None,
    Reference,
    Read,
    Operate,
    Manage
}
```

`None` means the resource should not be disclosed to the caller. `Reference`
means the resource may appear as a locked or redacted relationship node when it
is needed to explain an authorized resource, topology edge, trace, health
rollup, or dependency, but the caller cannot inspect details. `Read` allows
inspection of the resource and its non-secret operational data. `Operate`
allows resource operations and includes read-level inspection. `Manage` allows
administrative resource management and remains the compatibility superset for
resource actions.

Do not use `ResourceVisibility` for caller permissions. A hidden diagnostic
resource can still be readable by an operator with the right permission, and a
normal resource can still be only a locked reference for another caller.

## Endpoints

`Resource.Endpoints` contains `ResourceEndpoint` values.

```csharp
public sealed record ResourceEndpoint(
    string Name,
    string Protocol,
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    int? TargetPort = null);
```

Current meaning:

| Field | Meaning |
| --- | --- |
| `Name` | Stable endpoint name within the resource, such as `http`, `https`, `tds`, or `metrics`. |
| `Protocol` | Protocol name such as `http`, `https`, `tcp`, or `udp`. |
| `Exposure` | Intended visibility scope. |
| `TargetPort` | Resource-owned target port, if known. |

Exposure scope:

```csharp
public enum ResourceExposureScope
{
    Private,
    Local,
    Network,
    Public
}
```

Endpoint contracts should originate from `ResourceEndpointDescriptor` on the
resource type or instance. `ResourceEndpoint` is the current projected shape for
the resource-owned contract; concrete reachable addresses belong to endpoint
network mappings.

Endpoint is a runtime/networking concept, not a graph-native primitive. When a
runtime or networking provider needs to persist endpoint contracts in the
Resource graph/configuration model, it should contribute the endpoint shape and
mapped value type, then represent endpoint contracts as typed complex attribute
values, typically in an `endpoints` collection declared by the resource type
definition. The complex value carries the resource-owned contract fields, such
as endpoint name, protocol, target port, and exposure. It should not carry a
concrete reachable address unless the attribute is explicitly provider-managed
state for an endpoint network mapping.

## Endpoint Descriptors

Resource type contributions can declare endpoint descriptors. These describe
what instances of that type can expose by default before a concrete address is
assigned.

The descriptor includes:

- endpoint name
- protocol
- target port
- default exposure
- assignment default
- whether provider-supported port remapping is allowed

Descriptors are metadata. They are not runtime bindings and they do not own
network addresses.

## Endpoint Requests

Endpoint assignment intent is represented by `ResourceEndpointRequest`.

```csharp
public sealed record ResourceEndpointRequest(
    string Name,
    ResourceEndpointProtocol Protocol,
    int? TargetPort = null,
    string? Host = null,
    int? Port = null,
    string? IPAddress = null,
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    ResourceEndpointAssignment Assignment = ResourceEndpointAssignment.ProviderDefault,
    string? NetworkResourceId = null,
    string? ProviderEndpointId = null);
```

```csharp
public enum ResourceEndpointAssignment
{
    Manual,
    Auto,
    ProviderDefault,
    Predefined
}
```

Endpoint requests are intent, not observed state. They ask a network or
provider to assign, reserve, or use an address for an endpoint.

When endpoint requests are stored in the Resource graph/configuration model,
they should also be provider-contributed complex attribute values.
Assignment-specific fields such as host, port, IP address, assignment mode,
network reference, and provider endpoint ID belong to the request value.
References to resources should use the Resource model `ResourceReference`
primitive rather than raw resource ID strings.

## Endpoint Network Mappings

Topology-specific reachable addresses are represented by
`ResourceEndpointNetworkMapping`.

```csharp
public sealed record ResourceEndpointNetworkMapping(
    string Id,
    string Name,
    ResourceEndpointReference Target,
    string Address,
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    string? NetworkResourceId = null,
    string? ProviderResourceId = null,
    string? SourceEndpointName = null);
```

Use `ResourceEndpointNetworkMapping.ForEndpoint(...)` when constructing a
mapping for a target resource endpoint. It centralizes the canonical mapping ID
shape, target reference, and source endpoint name defaults:

```csharp
ResourceEndpointNetworkMapping.ForEndpoint(
    resourceId,
    endpointName,
    address,
    exposure,
    networkResourceId,
    providerResourceId);
```

These mappings are projected on the target resource through:

```csharp
Resource.ResourceEndpointNetworkMappings
```

Use them for:

- current reachable address display
- copy/open URL behavior
- runtime startup values such as `ASPNETCORE_URLS`
- showing where a resource endpoint is reachable in a topology

Resources do not synthesize endpoint network mappings from endpoint contracts.
If a provider needs a concrete reachable address, it must project a
`ResourceEndpointNetworkMapping`.

## Configured Endpoint Mappings

Configured source-to-target mappings are represented by
`ResourceEndpointMappingDefinition`.

```csharp
public sealed record ResourceEndpointMappingDefinition(
    string Id,
    string Name,
    ResourceEndpointReference Source,
    ResourceEndpointReference Target,
    string? NetworkResourceId = null,
    string? ProviderResourceId = null);
```

These mappings are normally projected on network resources through:

```csharp
Resource.ResourceEndpointMappings
```

They are different from endpoint network mappings:

| Object | Projected on | Meaning |
| --- | --- | --- |
| `ResourceEndpointNetworkMapping` | Target resource | Current topology-specific resolved address for one resource endpoint. |
| `ResourceEndpointMappingDefinition` | Network resource | Configured source endpoint -> target endpoint relationship, optionally materialized by a provider. |

Consumers that need a reachable address for a resource endpoint should resolve
the endpoint's network mapping by endpoint name.

In the Resource graph/configuration model, configured endpoint mappings should
be provider-contributed complex values that contain endpoint references: a
`ResourceReference` plus an endpoint name for the source and target. They may
also include optional network and provider references. Runtime materialization
status, observed conflicts, and concrete reachable addresses should be
projected by provider capabilities, operations, or read-only/provider-managed
graph attributes rather than authored as caller-managed mapping configuration.

Network topology and internet reachability are graph projections over these
resource-owned networking facts. A Resource graph view can choose to include a
network topology overlay that shows network resources, endpoint mappings,
published names, load-balancer routes, and internet connection facts. The same
facts can be projected into the Environment Map with runtime context such as
orchestration service boundaries, replica groups, replicas, and routing
bindings. The resource model should provide the relationships and attributes;
the map projection decides whether to render the normal resource view, the
runtime view, or the network-focused overlay.

Implicit default resources, such as the Host network or default Docker host,
are still resources in the realized model. The normal Resource graph should
hide them by default to keep declared intent readable, but it can expose a
toggle for showing implicit/default resources when the user needs the complete
realized resource set.

Internet reachability should be explicit or observed, not inferred from local
endpoint exposure alone. Local development resources can expose `localhost`
ports and still not have verified internet connectivity. Providers, runtime
observers, or network resources can project `internet.reachability` or
`network.internetReachability` with values such as `verified`, `reachable`, or
`inferred`; graph views can then show a reachability badge on that resource or
network.

## Actions

`ResourceActions` exposes operations a resource supports.

Standard IDs:

```csharp
public static class ResourceActionIds
{
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Pause = "pause";
    public const string Restart = "restart";
}
```

Actions are domain operations, not UI buttons. The Control Plane validates
state, authorization, provider support, and action capabilities before
execution.

Helper properties on `Resource`:

```csharp
public ResourceAction? StartAction => GetAction(ResourceActionIds.Start);
public ResourceAction? StopAction => GetAction(ResourceActionIds.Stop);
public ResourceAction? PauseAction => GetAction(ResourceActionIds.Pause);
public ResourceAction? RestartAction => GetAction(ResourceActionIds.Restart);
```

## Capabilities

Capabilities are roles a resource can play.

```csharp
public sealed record ResourceCapability(
    string Id,
    IReadOnlyDictionary<string, string>? Metadata = null);
```

Examples:

- `endpoint.source`
- `environment.variables`
- `networking.endpointProvider`
- `networking.endpointMapper`
- `networking.loadBalancer`
- `networking.namePublisher`
- `storage.provider`
- `storage.volume`

Capabilities are not actions. They help the Control Plane, UI, and providers
select resources by role without hard-coding resource types.

## Attributes

`Attributes` is a string dictionary of stable, non-secret projected facts.

Examples:

- `project.path`
- `container.image`
- `container.revision`
- `network.kind`
- `storage.medium`
- `storage.runtimeStatus`
- `storage.runtimeStatusReason`
- `dns.zone`
- `loadBalancer.routes`

Rules:

- Use dotted lower-camel names.
- Use invariant string formatting.
- Do not put secrets in attributes.
- Do not use attributes as a provider configuration schema.
- If a value needs validation, lifecycle semantics, secrecy, or structured
  behavior, use a typed object or provider-owned configuration instead.

Convenience helper:

```csharp
public IReadOnlyDictionary<string, string> ResourceAttributes =>
    Attributes ?? EmptyAttributes;
```

## Health And Observability

`HealthChecks` contains provider-projected health signals. Health is distinct
from lifecycle state. Liveness is an observation about whether a resource or
runtime scope is alive enough to participate in lifecycle and recovery policy;
health is an assessment over liveness, readiness, dependency checks,
provider-owned status, and any aggregate signal the resource or provider
exposes.

Health checks are registered on resources so the Control Plane can poll them
for the Resource Manager experience. The latest result is used for resource
status, while retained `ResourceHealthSummary` snapshots provide resource-keyed
history that resource-scoped and environment-wide Health views can chart and
correlate by resource and check.

Resource health check declarations are metadata for health assessments exposed
by the resource. Those assessments may represent only the resource, or they
may be resource-owned aggregate endpoints. For example, a frontend HTTP health
endpoint might return JSON that includes the frontend process status, backend
API reachability, SQL Server connectivity, and per-replica status. CloudShell
health declarations let the Control Plane discover and poll that assessment;
the resource or provider still owns the endpoint shape, payload, and any
resource-local aggregation model. A web application commonly exposes its own
liveness observation separately from a broader health assessment; dependency
health reported by that application should not automatically become the
application's own liveness unless the application or provider deliberately
models it that way.

For container app style resources, the health declaration belongs to the
stable container app resource, not to each replica as an independent top-level
resource. For replicated container apps, that declaration is a template for
the checks that should run against the materialized runtime replicas. The
container app resource remains the stable workload, exposure, and projection
boundary, while the runtime replica resources carry the concrete health and
liveness checks that CloudShell polls. The Control Plane can then derive the
container app's health assessment or liveness result from the per-replica
observations, with per-replica details available for Health, Monitoring, and
Degradation drill-downs.

A declared health check is not the same thing as a materialized probe target.
The declaration says which signal should be observed. The materialized target
is the runtime-specific way the Control Plane can evaluate that signal now:
an endpoint network mapping, an absolute URL, a provider-native evaluator, or
a future worker-owned target inside the runtime network. If a resource or
runtime scope has a declaration but no materialized target, the result should
be unresolved instead of being treated as an unhealthy response.

The current container app implementation projects replicated HTTP checks onto
the materialized hidden runtime replica resources. Active local Docker replicas
project probe-only endpoint mappings for the declared HTTP probe endpoints,
while the stable container app keeps the user-facing service endpoint and
ingress mapping. Stopped replicas keep the declared check contracts but do not
project reachable endpoint mappings. The Control Plane materializes an aggregate
health summary on the stable container app resource from the observed replica
checks, including unresolved observations when a provider cannot materialize a
probe target.

Those runtime-scope probe targets do not change the containment boundary.
Replicas remain hidden, runtime-managed child resources owned by the container
app. They can contribute liveness observations and health details, but the
container app remains the lifecycle, recovery, exposure, configuration, and
normal management target. A failed replica observation can degrade the
container app aggregate, while recovery policy should act on the stable
container app unless a future provider explicitly models replica-scoped
recovery operations.

Application-like resources share much of the same provider toolkit, and every
declared resource can participate in liveness observations and health
assessments regardless of whether it is currently user-managed, platform-owned,
or provider-managed. The distinction appears when providers also project
runtime resources. A single local executable, project-backed application, or
service resource can expose checks directly on the declared resource. A
replicated container app is different: the declared application resource is
the workload, exposure, and projection boundary, while materialized runtime
replicas are the concrete probe targets. The shared application resource model
should therefore support checks declared directly on resources and checks
projected onto runtime resources, with the provider deciding which projection
fits the resource type.

Future Control Plane health endpoints can expose CloudShell-computed health
scopes. A health scope is a Control Plane aggregation boundary built from the
state CloudShell has already collected by polling resource health checks,
liveness signals, readiness signals, provider-owned status, monitoring data,
and other resource factors. For example, a health scope could represent the
frontend and its declared dependencies, a container app and all replicas, or a
curated group of resources that should be reported together for a local
application topology. The scope names the boundary; a future scope definition
or provider-specific implementation can decide which observed signals
contribute to the aggregate result.

Managed health scopes can later be configured from the global Health surface.
An operator could create a scope, add resources or resource health checks to
that scope, choose which observed signals or provider factors contribute, and
let the Control Plane expose the computed aggregate through a
CloudShell-provided health endpoint. Resource-owned health checks remain
visible on their individual resources; scopes are a higher-level grouping over
those observed resource checks and related signals.

`ResourceHealthCheck` is the shared health-signal declaration. Its
`ResourceProbeType` says how the signal should be interpreted, such as health,
liveness, readiness, or startup. Its `ResourceProbeSource` says where the
signal comes from. HTTP is the built-in source today, but providers can add
non-HTTP evaluators for process, container, runtime, or provider-native
signals without turning every resource health check into an HTTP endpoint.
`ResourceHealthCheckResult` can carry scoped observations returned by the
evaluator. Scoped observations describe the boundaries that contributed to an
aggregate result, such as runtime replicas, dependencies, routes, selected
resource sets, or provider-defined service scopes. They are observation data
from polling, not declaration metadata on the resource health check.

`Observability` contains resource observability metadata. If missing, it
defaults to `ResourceObservability.None` through `EffectiveObservability`.
`ResourceObservability` can also advertise telemetry sources and scopes. A
source describes how a resource produces or exposes telemetry, such as a
provider-owned stream, an OpenTelemetry exporter, or a
Prometheus/OpenMetrics-style endpoint. A scope describes a selectable
provider-defined unit underneath the stable
resource, such as a replica, partition, worker, shard, or runtime container.

Logs, resource events, and traces are not embedded in `Resource`. They are
queried through managers such as `ILogManager`, `IResourceEventManager`, and
`ITraceManager`.

Resource-facing operational signals use the shared
`ResourceSignalSeverity` vocabulary: `Success`, `Info`, `Warning`, and
`Error`. Resource events expose typed severity in the domain model; API and
persistence projections serialize that severity as stable strings for
transport and storage compatibility. Resource Manager diagnostics and callouts
use the same severity vocabulary so providers, Control Plane behavior, and UI
surfaces do not drift into separate string conventions.

## Identity

`Identity` contains optional `ResourceIdentityBinding` metadata. It describes
the resource's identity configuration and provider binding; it is not the
grant subject used by access control.

Identity metadata is non-secret. Runtime credentials are transferred through
safe provider mechanisms such as environment variables, token endpoints,
mounted configuration, or platform-managed identity facilities.

Resource permissions and grants are evaluated by the Control Plane. A resource
identity can act on another resource only when the relevant grant or permission
exists.

Identity provider configuration is an environment capability. A resource's
`Identity` binding is separate per-resource intent and may resolve through the
environment default provider.

Access control uses principals. A resource identity is one kind of principal,
and programmatic declarations expose it through `resource.Principal` when a
resource needs to be granted access to another resource. User principals,
groups, service accounts, workload identities, managed identities, and
provider-owned identity references use the same principal-to-resource grant
model. Resource events should preserve the acting principal so activity logs
can show which resource or user triggered an operation.

## Load-Balancer Routes

`LoadBalancerRoutes` contains route definitions projected by load-balancer
resources.

```csharp
public IReadOnlyList<LoadBalancerRoute> ResourceLoadBalancerRoutes =>
    LoadBalancerRoutes ?? [];
```

Routes are resource data for load-balancer resources. They should not replace
resource endpoints or configured endpoint mappings.

## Null Collection Normalization

Most optional collections have helper properties that normalize `null` to an
empty collection:

```csharp
ResourceActions
ResourceHealthChecks
ResourceCapabilities
ResourceEndpointMappings
ResourceEndpointNetworkMappings
ResourceLoadBalancerRoutes
```

Consumers should use these helpers instead of reading nullable constructor
properties directly.

## Resource And Service Terminology

CloudShell resources often contain or provide services. Keep the terms
separate:

| Term | Meaning |
| --- | --- |
| Resource | The CloudShell management object described in this document. |
| Service | The runtime capability, process, API, or infrastructure behavior provided by a resource. |
| Service resource | The specific `cloudshell.service` resource kind. |

Examples:

- A container app resource provides an application service.
- A SQL Server resource provides a database service.
- A Secrets Vault resource provides a protected secrets API.
- A load balancer resource provides a routing service.
- A `cloudshell.service` resource is only used when the user explicitly models
  a service facade or service unit as a resource.

## Provider Ownership

CloudShell-owned resource data:

- identity, name, type, class
- registration and grouping
- dependencies and parent/owner relationships
- actions and action capability results
- endpoint descriptors, requests, mappings, and projected endpoint facts
- permissions, resource events, and API projection

Provider-owned data:

- provider configuration
- runtime state
- external system calls
- credentials and secrets
- implementation containers/processes
- provider-native diagnostics

Providers project only stable, non-secret facts into `Resource`.

## Current Cleanup Targets

The object model already contains the core mapping concepts, but a few areas
should be revisited before API stability:

- Remove remaining endpoint-address compatibility factories once samples and
  provider declarations have shifted to descriptor and mapping-native APIs.
- `cloudshell.service`, `ResourceOrchestratorService`, and provider-native
  service terminology should stay explicitly separated in docs and APIs.
- Large cross-resource concerns may eventually need typed facets, such as
  `ResourceNetworking`, `ResourceStorage`, `ResourceIdentity`, or
  `ResourceRuntime`, instead of continuing to grow the root `Resource` record.
