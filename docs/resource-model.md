# Resource Model

This document describes the low-level CloudShell resource object model: the
stable parts that make up a resource, what each part means, and how related
objects such as endpoints, mappings, actions, capabilities, ownership metadata,
and attributes fit together.

For broader product concepts, see [Domain model](domain-model.md).

## Resource Projection

The low-level resource object is a projection returned by the Control Plane and
consumed by Resource Manager UI, remote clients, and extensions. It is not the
runtime object itself. It is the current known description of a managed
resource.

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

## Endpoints

`Resource.Endpoints` contains `ResourceEndpoint` values.

```csharp
public sealed record ResourceEndpoint(
    string Name,
    string Address,
    string Protocol,
    ResourceExposureScope Exposure = ResourceExposureScope.Local,
    int? TargetPort = null);
```

Current meaning:

| Field | Meaning |
| --- | --- |
| `Name` | Stable endpoint name within the resource, such as `http`, `https`, `tds`, or `metrics`. |
| `Address` | Compatibility projection of a currently reachable address. Prefer endpoint network mappings when available. |
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
resource type or instance. `ResourceEndpoint` is the current projected shape and
still carries `Address` for compatibility.

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

These mappings are projected on the target resource through:

```csharp
Resource.ResourceEndpointNetworkMappings
```

Use them for:

- current reachable address display
- copy/open URL behavior
- runtime startup values such as `ASPNETCORE_URLS`
- showing where a resource endpoint is reachable in a topology

Compatibility fallback:

```csharp
public IReadOnlyList<ResourceEndpointNetworkMapping> ResourceEndpointNetworkMappings =>
    EndpointNetworkMappings ?? CreateEndpointNetworkMappings(Id, Endpoints);
```

If explicit network mappings are not projected, the helper creates synthetic
mappings from endpoint addresses. This preserves older endpoint-address-based
providers while the model shifts toward explicit mappings.

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
the endpoint's network mapping by endpoint name. This preserves compatibility
with legacy endpoint-address projections while keeping new provider behavior
centered on explicit mappings.

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
from lifecycle state.

`Observability` contains resource observability metadata. If missing, it
defaults to `ResourceObservability.None` through `EffectiveObservability`.

Logs, resource events, and traces are not embedded in `Resource`. They are
queried through managers such as `ILogManager`, `IResourceEventManager`, and
`ITraceManager`.

## Identity

`Identity` contains optional `ResourceIdentityBinding` metadata.

Identity metadata is non-secret. Runtime credentials are transferred through
safe provider mechanisms such as environment variables, token endpoints,
mounted configuration, or platform-managed identity facilities.

Resource permissions and grants are evaluated by the Control Plane. A resource
identity can act on another resource only when the relevant grant or permission
exists.

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

- `ResourceEndpoint.Address` is compatibility state. Endpoint network mappings
  should become the canonical address projection.
- `cloudshell.service`, `ResourceOrchestratorService`, and provider-native
  service terminology should stay explicitly separated in docs and APIs.
- Large cross-resource concerns may eventually need typed facets, such as
  `ResourceNetworking`, `ResourceStorage`, `ResourceIdentity`, or
  `ResourceRuntime`, instead of continuing to grow the root `Resource` record.
