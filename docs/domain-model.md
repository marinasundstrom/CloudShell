# Domain model

This document explains the core CloudShell concepts, where each concept is
modeled in code, and how the concepts are projected through the Control Plane
API.

CloudShell deliberately uses different levels of abstraction, but those levels
should use the same established concepts. Internal Control Plane services are
more granular because they coordinate providers, stores, and authorization.
Shell and integration consumers use higher-level managers. The HTTP API
projects the same domain entities through versioned contracts with hypermedia
affordances.

## Abstraction levels

| Level | Audience | Purpose | Examples |
| --- | --- | --- | --- |
| Product concepts | Users and extension authors | Describe what CloudShell manages and shows | resources, resource groups, lifecycle actions, logs |
| Public domain abstraction | Shell integrations and remote adapters | Cloud-plane client API for the Control Plane domain without caring about transport | `IResourceManager`, `IResourceTemplateManager`, `ILogManager`, `Resource` |
| Internal Control Plane services | Control Plane implementation | Coordinate state, providers, persistence, authorization, and procedures | `InProcessControlPlane`, `IResourceManagerStore`, `IResourceRegistrationStore`, `ResourceOrchestrationService` |
| Provider contracts | Provider and extension packages | Project external systems into CloudShell and execute provider-owned operations | `IResourceProvider`, `IResourceCreationProvider`, `IResourceProcedureProvider`, `IResourceTemplateProvider` |
| HTTP API projection | Remote Control Plane clients and generated clients | Versioned contract for the same domain entities and relationships | `/api/control-plane/v1/resources`, `ResourceResponse`, `resourceActions` |
| UI projection | Shell UI and extension views | Render resources and operations for users | Resource Manager pages, detail routes, provider-owned views |

The higher-level public abstraction is the cloud-plane client API. It should be
less granular than the internal Control Plane implementation while still using
the same domain concepts. A consumer asks the domain manager to list resources
or execute a resource action. It should not need to compose registration
stores, provider stores, route templates, and generated HTTP clients itself.

## Core concepts

### Resource

A resource is the central CloudShell artifact. It represents something the
platform can inspect or operate, such as a Docker Engine, container, executable
application, configuration service, database, queue, or internal service.

In code, a resource is projected as `Resource`.

Important properties:

- `Id`: stable identifier.
- `TypeId` / `EffectiveTypeId`: stable resource type.
- `ResourceClass`: broad resource classification such as executable, project,
  container, service, network, configuration, or infrastructure.
- `Attributes`: stable, non-secret details that describe the resource's class,
  type, or provider-owned shape.
- `State`: lifecycle or health-oriented state.
- `Endpoints`: addresses exposed by the resource.
- `DependsOn`: resource dependencies.
- `ParentResourceId`: parent/child hierarchy.
- `DetailRoute`: optional extension-owned UI detail route.
- `ResourceActions`: resource-domain operations exposed by the provider.
- `ResourceHealthChecks`: health signals contributed by providers.

`Resource` is a uniform projection. It is not subclassed for containers,
executables, projects, services, or infrastructure. A resource carries common
attributes such as class, type, endpoints, actions, health checks,
observability, and structural metadata; providers own the configuration and
runtime behavior behind those attributes. `Resource` does not imply CloudShell
owns all underlying provider configuration or runtime state.

`Attributes` are not a second provider configuration schema. They are projected
facts useful for inspection, filtering, diagnostics, and orchestration hints,
such as container image, workload kind, endpoint count, service port count, or
configuration entry count. Providers must not expose secrets through resource
attributes.

Consumers can filter resource lists by `ResourceClass` when they need broad
class-level views, such as all container-backed resources or all logical
services, without relying on provider-specific `TypeId` values.

As a client API entity, `Resource` should be convenient to inspect without
becoming an active service object. It may expose domain helpers such as
case-insensitive resource-action lookup and standard lifecycle action
properties. It should not execute operations itself. Commands still go through
`IResourceManager`, which can represent either the in-process Control Plane or a
remote API-backed adapter.

### Resource type

A resource type identifies a kind of resource and, when appropriate, the UI for
adding or updating that resource.

Resource types are extension contributions. They are user-facing and stable
across providers and hosts.

Examples:

- `docker.engine`
- `application.executable`
- `configuration.store`
- `cloudshell.network`
- `cloudshell.service`

Resource type registration is separate from resource discovery. A provider can
discover available resources, while a resource type contribution describes how a
user can add or configure a resource of that type.

### Resource provider

A resource provider is an internal implementation service. It maps an external
system or provider-owned configuration into `Resource` projections.

Providers implement contracts such as:

- `IResourceProvider`: lists projected resources.
- `IResourceCreationProvider`: creates/registers resources from domain
  creation commands.
- `IResourceProcedureProvider`: executes provider-owned procedures such as
  lifecycle actions.
- `IResourceTemplateProvider`: exports/imports provider-owned template payloads.

Providers are not a product concept shown directly in the UI. They are part of
the Control Plane implementation.

### Resource registration

A registration is platform-owned state saying that a resource should be visible
and managed through CloudShell.

In code, registration state is represented by `ResourceRegistration` and stored
through `IResourceRegistrationStore`.

Registration tracks:

- resource ID
- provider ID
- optional resource group ID
- registration time
- platform-declared dependencies

Provider discovery alone does not make a root resource visible in Resource
Manager. A root resource becomes visible when it is registered. Dynamic children
can appear under a registered parent.

### Resource group

A resource group is a user-managed project boundary and authorization scope.

Resource groups are platform-owned state. Providers do not own group semantics.

Sub-resources inherit grouping through their parent or registration path. Group
membership affects filtering, dependency candidate selection, and resource-scope
authorization.

### Dependency

A dependency is a relationship where one resource relies on another.

Dependencies can come from:

- provider projection, such as a service depending on a network
- platform registration metadata
- programmatic resource declarations

The projected resource dependency list should be normalized and stable.
Dependency behavior is owned by the Control Plane, especially when actions need
to start dependencies or warn about active dependents.

### Resource action

A resource action is a domain operation on a resource.

Standard lifecycle actions use `ResourceActionKind`:

- `Run`
- `Stop`
- `Pause`
- `Restart`

Providers can also expose custom actions with stable IDs.

Resource actions are not UI actions. A UI button or menu item may render a
resource action, but the UI element is only a presentation of the resource
operation.

The provider declares the action surface on `Resource.ResourceActions`.
The Control Plane validates state, authorization, provider support, and other
constraints before dispatching execution.

The public abstraction defines canonical action IDs for standard lifecycle
actions. Consumers should use those IDs and `Resource` action lookup
helpers instead of hard-coding string literals or route templates. The list of
actions on a resource means "this operation exists for this resource"; it does
not mean the current caller can execute the operation right now.

### Resource action capability

A resource action capability describes whether a resource action can currently
be executed and why.

In code, this is modeled through:

- `ResourceOperationCapabilities`
- `ResourceActionCapability`

Capabilities are separate from actions:

- Resource action: the operation that exists on the projected resource.
- Resource action capability: current ability to execute that operation.

Capability decisions can combine authorization, resource state, provider
support, dependency warnings, and other operational constraints.

As a client API convenience, `ResourceOperationCapabilities` can expose
case-insensitive action-capability lookup and standard lifecycle booleans. This
keeps the consumer workflow explicit:

1. Inspect `Resource.ResourceActions` to discover the resource operation.
2. Inspect `ResourceOperationCapabilities` to decide whether it can execute now.
3. Call `IResourceManager.ExecuteResourceActionAsync` to request execution.

The public abstraction may provide manager extension methods for common
lifecycle operations, such as run, stop, pause, and restart. These helpers
should construct domain commands for `IResourceManager`; they should not move
execution behavior onto `Resource`.

Client convenience APIs can also provide singular capability lookup helpers for
a resource. Those helpers are still manager operations because the Control Plane
owns authorization and state decisions. The resource projection remains passive.

### Log

A log is an operational stream or historical source exposed by a provider or
extension.

Logs are not embedded fields on a resource. A log descriptor can point at a
resource ID, artifact ID, or provider-owned source.

In code:

- `ILogManager` is the public domain abstraction.
- `ILogStore` is the internal Control Plane implementation store.
- `ILogProvider` is the provider contract.

### Template

A resource group template is a portable group-level envelope owned by
CloudShell. Individual resource payloads inside the template are provider-owned.

In code:

- `IResourceTemplateManager` is the public domain abstraction.
- `ResourceTemplateService` orchestrates import/export.
- `IResourceTemplateProvider` owns per-resource import/export behavior.

This preserves the ownership split: CloudShell owns grouping and orchestration;
providers own their resource configuration schema.

## Projection into the API

The Control Plane API projects the domain for remote clients. It is a
transport/versioning contract, not the internal implementation model.

The contract should resemble the domain model. API DTOs are contract entities
for the established domain entities and relationships. They should use the same
conceptual nouns as the public domain abstraction, then add transport
affordances where useful.

For example, an API resource is still a resource, a resource action is still a
resource action, a resource group is still a resource group, and a capability is
still a capability. The HTTP projection may add `href`, `method`, and versioned
route details, but it should not rename domain concepts into generic Web API
operations or expose internal provider/store granularity.

### Resource response

`GET /api/control-plane/v1/resources` returns projected resources.

The API resource response includes:

- identity and descriptive fields
- state
- endpoints
- dependencies
- parent/resource group information
- registration signal
- `resourceActions`

`resourceActions` is a dictionary keyed by resource action ID. Each value is a
hypermedia affordance:

```json
{
  "id": "service:api",
  "name": "API",
  "typeId": "application.executable",
  "state": "Running",
  "resourceActions": {
    "stop": {
      "id": "stop",
      "displayName": "Stop",
      "kind": "Stop",
      "method": "POST",
      "href": "/api/control-plane/v1/resources/service%3Aapi/actions/stop",
      "requiresConfirmation": true
    }
  }
}
```

The action affordance tells clients how to invoke the operation without knowing
route templates out of band.

### Capabilities response

`POST /api/control-plane/v1/resources/capabilities` returns current
capabilities for selected resources.

The capability response includes:

- `canManage`
- `canDelete`
- `executableActionIds`
- `resourceActionCapabilities`

`resourceActionCapabilities` is the richer signal. `executableActionIds` is a
compact projection for common UI and adapter checks.

### Commands

Commands in the public domain abstraction are intent-shaped:

- `CreateResourceCommand`
- `RegisterResourceCommand`
- `AssignResourceGroupCommand`
- `SetResourceDependenciesCommand`
- `ExecuteResourceActionCommand`

The HTTP API maps these commands to routes and request DTOs. Consumers should
prefer the manager abstraction unless they are implementing or generating an
HTTP adapter.

## Granularity rules

Use this rule of thumb when adding or changing concepts:

- If it is visible to shell integrations, model it in the public domain
  abstraction.
- If it coordinates providers, stores, persistence, or authorization, keep it in
  internal Control Plane services.
- If it is provider-owned configuration or runtime state, keep it behind the
  provider contract and project only the useful resource/log/template signals.
- If it is a remote transport concern, keep it in the API DTO/client adapter.
- If it is only presentation, keep it in the UI layer or extension UI.

Do not leak internal store/provider granularity into client-facing abstractions.
Do not model UI actions as resource actions. Do not force API consumers to know
route templates when the operation belongs to a projected artifact.

When adding API fields, prefer names that match the public domain concept unless
there is a clear transport reason not to. When a generated client maps back to
the domain abstraction, the mapping should be shallow and obvious rather than a
semantic translation layer.

If a proposed API contract needs a concept that does not exist in the domain
model yet, first decide whether the domain model is missing an entity,
relationship, or capability. Add the domain concept deliberately, then project
it through the API contract.
