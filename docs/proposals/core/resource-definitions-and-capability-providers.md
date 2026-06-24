# Resource Definitions, Capability Providers, and Operation Providers Proposal

## Status

POC in progress.

CloudShell already distinguishes projected resources from declared resources in
the resource model documentation, and several providers already carry typed
definition records such as application, storage, volume, network, service, DNS,
and load-balancer definitions. This proposal tracks the next model step:
formalizing `Resource` as the resolved core projection, formalizing
`ResourceDefinition` as its interchange model/format, and formalizing
capability providers and operation providers as attached behavior over the
resolved resource.

The first implementation slice is isolated in `CloudShell.ResourceDefinitions`
with tests in `CloudShell.ResourceDefinitions.Tests`. It proves the
interchange envelope, class/type inheritance, effective
attribute/capability/operation resolution, diagnostics, and provider-dispatch
contracts without changing the Control Plane pipeline, persistence, API
projection, or existing provider definition stores.

## Problem

`Resource` is the current known projection of a managed artifact. It is what
Resource Manager, the Control Plane API, providers, and remote clients can
inspect after provider behavior has accepted, normalized, or observed resource
state.

Resource declarations, templates, persistence flows, imports, and create flows
need an interchange artifact. They describe a resource state snapshot or
change that can be applied to a `Resource`, but they are not the source from
which a `Resource` is projected. Today that structure exists in several
partly overlapping forms:

- programmatic `ResourceDeclaration`
- provider-specific typed records such as `ApplicationResourceDefinition`,
  `VolumeResourceDefinition`, and `NetworkResourceDefinition`
- resource template entries with `JsonElement Configuration`
- create requests with `JsonElement Configuration`
- projected `Resource.Attributes`

This makes it easy for definition, projection, configuration, and diagnostics
to blur together. It also tempts the platform to put complex resource
configuration into projected attributes, even though attributes are currently
documented as stable, non-secret projected facts.

Resource definitions also need inherited expectations. A resource instance can
inherit attributes, capabilities, and operations from its
`ResourceTypeDefinition`, and that type definition can in turn inherit from a
broader `ResourceClassDefinition`. Raw property bags such as `.Attributes`,
`.Capabilities`, and `.Operations` therefore cannot be treated as the
effective model. They are declared or observed inputs that need resolution
against class, type, preset, provider, and environment rules.

Capabilities have a related issue. A resource type may support a capability,
an individual resource may define capability-owned state, and the resolved
resource may advertise a capability that downstream systems can discover.
Those are related, but they are not the same lifecycle phase.

Resource commands and operations have the same boundary concern. A projected
resource can expose commands such as start, stop, restart, reconcile,
update-image, or a provider-specific command. A command is the thing a caller
performs. Operations are declared on class definitions, type definitions, or
resource-owned state and add behavior to a resource. Some operations can be exposed as caller-facing
commands; other operations may exist mainly to drive validation, projection,
automation, reconciliation, or provider behavior. The behavior that validates
operation availability and executes or applies the backing behavior should not
have to live in a single monolithic resource type provider. The current
implementation may continue mapping command affordances onto the existing
action-shaped API fields during migration, but the durable domain language
should distinguish caller-facing commands from declared operations.

Other possible names for this concept are action, command, or procedure.
`Operation` is the most neutral term because it does not imply that the
behavior is always directly invoked by a user. The important point is that an
operation declaration adds behavior to a resource, and a provider supplies the
implementation for that behavior in the current environment.

## Goals

- Distinguish `Resource` projections from the `ResourceDefinition`
  interchange model/format in public domain language, docs, APIs,
  persistence, templates, imports, and provider contracts.
- Keep `Resource` as the resolved core projection of resource state.
- Define a plain serialized resource-definition interchange format that can be
  exchanged, reviewed, imported, and projected through templates without
  becoming provider-native configuration or the required persistence shape.
- Let resource types expose typed facades over definition payloads without
  requiring every consumer to understand every provider-specific type.
- Treat capability providers as attached behavior registered through
  dependency injection, so they can resolve provider or platform services while
  validating and interpreting resolved capabilities on `Resource`.
- Treat resource operation providers as attached behavior registered through
  dependency injection, so each provider can own the provider-side behavior
  behind one resolved resource operation.
- Define `ResourceClassDefinition` and `ResourceTypeDefinition` inheritance so
  attributes, capabilities, operations, defaults, presets, and requirements can
  be resolved before validation or projection.
- Define attribute validators for common rules and provider/type-specific
  rules, including required attributes and broader value validation.
- Provide resolver APIs that compute effective attributes, capabilities, and
  operations instead of asking callers to trust raw property bags.
- Separate resource-type validation from cross-cutting capability validation.
- Preserve provider ownership over runtime behavior, apply/update/delete
  behavior, and provider-specific configuration.
- Prevent secrets from being serialized into resource definitions, projected
  attributes, diagnostics, logs, templates, or generated code.

## Why This Model

The main advantage of this model is that it separates graph structure from
Control Plane operations. The Resource model owns declared resources,
relationships, resource-owned attributes, capability declarations, operation
declarations, and resolution rules. Resource Manager owns operational records,
liveness, lifecycle procedures, authorization-filtered views, logs, traces,
and provider runtime state. That lets each side evolve without forcing one
model to carry every concern.

The expected benefits are:

- Cleaner provider boundaries: provider packages can define resource types,
  attributes, capabilities, and operations without mixing those declarations
  with Resource Manager UI or operational state.
- A real declaration graph: declared resources and relationships can be
  resolved, validated, rendered, diffed, and applied instead of being inferred
  from ad hoc provider projections.
- Lazy resolution: Resource Manager can serve ordinary inspection from its own
  model and resolve the Resource model graph only when relationships,
  capabilities, operations, validation, planning, or graph changes require it.
- Behavior as model concepts: capabilities and operations become resolvable
  resource behavior with provider-owned implementations, not scattered helper
  methods or UI-shaped actions.
- Deliberate interchange: `ResourceDefinition` becomes an import, export,
  deployment, template, and debug format instead of the required internal
  runtime state container.
- Typed upper-domain APIs: future generated wrappers can expose typed
  properties and methods over the low-level `Resource` projection without
  duplicating state or introducing resource subclasses.
- Better persistence choices: CloudShell can persist resource-owned state,
  snapshots, or incremental changes without making the interchange document
  the database schema or choosing a backing store too early.
- Safer replacement path: the model can first integrate through adapters into
  the existing Resource Manager surface, then replace older provider and
  declaration paths only after the integration proves value.

## Non-Goals

- Do not subclass projected `Resource` for executable apps, container apps,
  volumes, databases, networks, services, or other resource types.
- Do not make projected `Resource.Attributes` a structured provider
  configuration schema.
- Do not require every provider-owned runtime artifact to be authorable as a
  resource definition.
- Do not require lossless round-tripping from every provider projection back
  into a resource definition.
- Do not replace provider-specific typed definitions immediately; the first
  step is an envelope and validation model that existing definitions can map
  into.
- Do not require the first implementation to settle the final resolver API
  shape. The durable requirement is that resolution exists and callers have a
  supported path to ask for effective values and diagnostics.
- Do not make capability providers UI actions. Capabilities may support UI
  workflows, but their model behavior belongs to the resource/domain layer.
- Do not make resource commands UI actions. They are resource-domain commands
  that UI or API surfaces may invoke after authorization and capability checks.
  Resource operation providers own what happens behind those commands.

## Proposed Model

At the highest level, the core runtime model should center on three
concepts: `Resource`, `ResourceTypeDefinition`, and
`ResourceClassDefinition`.
`Resource` represents the low-level projected state of a resource:
identity, type, attributes, capability declarations, operation declarations,
dependencies, provider-owned payloads, and actual resource state observed or
accepted by providers. `ResourceTypeDefinition` and
`ResourceClassDefinition` define shapes, presets, and expectations for
valid `Resource` instances.

The composition of a `Resource` is based on its `ResourceTypeDefinition`,
which is based on its `ResourceClassDefinition`, plus the resource's declared
attribute values, capabilities, and operations. Those inputs are merged by the
resource resolution rules. That is why `Resource` is a projection: callers see
the resolved `.Type`, `.Attributes`, `.Capabilities`, and `.Operations`, not
only the raw declarations. Some resolved values may still be lazy or queried
through resolvers when they depend on provider state, related resources, or
runtime observations.

CloudShell may also need `ResourceType` and `ResourceClass` views.
`ResourceTypeDefinition` resolves to `ResourceType`, and
`ResourceClassDefinition` resolves to `ResourceClass`. The definitions are
static declarations; they do not change and do not carry modifiable resource
state. `ResourceType` would include inherited class values, effective
attributes, supported capabilities, supported operations, presets, and
provider requirements. `ResourceClass` would be the resolved view of a
`ResourceClassDefinition`. A `Resource` can then expose or query its resolved
`.Type` and `.Class` views instead of forcing callers to inspect raw
definition objects.

The reason for having the `*Definition` classes is that they are the
declaration classes for interchange. `ResourceDefinition`,
`ResourceTypeDefinition`, and `ResourceClassDefinition` can be rendered to and
loaded from JSON, YAML, XML, templates, imports, or provider package
manifests, while `Resource`, `ResourceType`, and `ResourceClass` remain the
resolved runtime views used by normal domain code.

`ResourceDefinition` is still part of the model, but with a specific purpose:
it is the interchange model/format for a `Resource`, not another runtime
state container. The direction is explicit: take a `Resource` projection and
render it as a `ResourceDefinition` for interchange, review, deployment input,
import, or export; take a `ResourceDefinition` and apply it to a `Resource`
through provider-owned validation, planning, and behavior. Applying a
`ResourceDefinition` changes resource-owned state, not
`ResourceTypeDefinition` or `ResourceClassDefinition`. Capability providers
and operation providers interpret the resolved resource projection as
behavior: they can validate it, project helper data or command affordances
from it, resolve related resources, and produce changes.

Raw `ResourceDefinition` validation is a separate interchange/document
concern. It can check whether an authored document is well formed, references
known IDs, uses allowed fields, or contains capability/operation declarations
that are valid before a `Resource` exists. Capability providers and operation
providers are the runtime/domain behavior layer and should act on the
resolved `Resource`. If CloudShell supports both, the contracts should remain
separate so document validation does not become resource behavior and
resource behavior does not depend on raw interchange shape.

This proposal should distinguish the capability or operation from its
provider. A resolved `Capability` or `Operation` can live in the `Resource`
collections as model data with IDs, source information, availability, and
effective configuration. A `CapabilityProvider` or `OperationProvider` is the
behavior implementation for that resolved model entry in the current
environment.

Typed resource wrappers are a higher-level API over this low-level
projection. They may eventually be source generated from type definitions,
attribute IDs, capability IDs, and operation IDs, but they are not the
projection itself and must not become another state container.

CloudShell should use `ResourceDefinition` as the authored or exchanged
resource interchange model/format.

A resource definition interchange format should include:

- stable resource name or ID
- resource type
- optional provider ID when the type can be handled by more than one provider
- optional display name
- dependencies and references
- optional definition version
- provider-owned configuration payload
- capability-owned intent payloads
- optional operation declarations or operation configuration when a resource
  type allows authored operation policy
- non-secret platform metadata needed for registration, ownership, visibility,
  persistence, or grouping

A serialized projection might look like:

```jsonc
{
  "apiVersion": "cloudshell.resource/v1",
  "name": "api",
  "type": "application.executable",
  "provider": "applications.executable",
  "displayName": "API",
  "dependsOn": [
    {
      "value": "storage.volume:data",
      "relationship": "dependsOn",
      "addressingMode": "resourceId"
    }
  ],
  "configuration": {
    "executable": {
      "path": "dotnet",
      "arguments": "run",
      "workingDirectory": "./src/Api"
    }
  },
  "capabilities": {
    "storage.volumeConsumer": {
      "mounts": [
        {
          "volume": "volume:data",
          "targetPath": "App_Data",
          "readOnly": false,
          "name": "data"
        }
      ]
    }
  }
}
```

The serialized form is one rendering of a `Resource`. Code-first builders,
Resource Manager create flows, resource templates, imports, and future API
clients can all produce a `ResourceDefinition` interchange model that applies
to the same core resource model. `ResourceTypeDefinition` and
`ResourceClassDefinition` should also be renderable as plain JSON when they
need to be inspected, exchanged, or loaded from provider packages.

## Resource and ResourceDefinition

The core concern in this proposal is the Resource model. The Resource model
defines resources, their relationships, resource-owned attribute data,
capability declarations, and operation declarations. A resource graph is the
result: a graph defined using this model, resolved from the declared resources
and their relationships.

`Resource` is the concrete resolved projection in this model. It combines
`ResourceClassDefinition` and `ResourceTypeDefinition` presets with
resource-specific declared state, resolved attributes, resolved capabilities,
resolved operations, and accepted resource state. It is the low-level object
that typed wrappers can be built on, not the generated typed wrapper itself.
Most consumers should normally see this `Resource` projection rather than a
`ResourceDefinition`.

`ResourceDefinition` is the interchange model/format for a `Resource`. It is
used for rendering, validation at import boundaries, apply/update/delete
planning, template export, interchange, and deployment input. It should be
plain and serializable, but it should not be treated as the runtime state
container inside the model. The two primary operations are:

- render `Resource` as `ResourceDefinition`
- apply `ResourceDefinition` to `Resource`

Resource Manager is part of the Control Plane. It should manage a resource
graph that is defined using the Resource model. It may share identity with a
low-level `Resource`, may use that projection model, and may reference the
same accepted state, but it is a separate Control Plane artifact. Resource
Manager resources carry operational responsibilities such as liveness signal
realization, lifecycle state, materialization status, authorization-filtered
projection, endpoints, procedures, logs, traces, and provider-observed runtime
facts. Those concerns belong to the Resource Manager model and projection
pipeline, not to the core Resource model or its interchange format.

For the POC, the Resource model should be implemented far enough to produce
and resolve a resource graph: type/class inheritance, resolved attributes,
resolved capabilities, resolved operations, provider lookup, and
rendering/applying `ResourceDefinition`. It should prove declaration,
relationship, stored attribute data, capability declaration, and operation
declaration semantics. It should not try to become the Resource Manager model.
Resource Manager concerns such as liveness realization, lifecycle execution,
authorization-specific views, operational history, endpoint materialization,
logs, and traces should remain above this model.

That is the core POC concern. The immediate implementation should stay focused
on declaring resources, storing their declared state, expressing their
relationships, defining capabilities and operations, resolving the resulting
graph, and committing accepted declaration-state changes. Runtime operation
execution, liveness materialization, provider reconciliation, authorization
views, and operational history are consumers or later layers over that graph,
not reasons to expand the Resource model now.

The next useful POC question is therefore Resource Manager integration, not
the final data store. Resource Manager should be able to manage a resource
graph defined by the Resource model while continuing to own Control Plane
concerns such as registration, grouping, liveness, lifecycle procedures,
authorization-filtered views, logs, traces, and provider runtime metadata. The
integration slice should show where a resolved Resource model graph enters the
existing Resource Manager composition path, how it maps to the current
Resource Manager-facing `Resource` projection, and which operational state
remains Control Plane-owned. Only after that boundary is proven should the POC
optimize the backing store shape.

Resource Manager should also remain the normal entry point for users and API
consumers. Most reads can start from the Resource Manager model and its
Resource Manager-facing resources without resolving the full Resource model
graph. The graph should be resolved when a workflow needs graph-aware behavior:
relationship traversal, inherited type/class values, capability or operation
resolution, validation, planning, or a change that updates declared resource
state. This keeps ordinary inspection and operational views on the Resource
Manager side, while reserving Resource model resolution for the cases where
the model's relationships and declaration semantics are actually needed.

In effect, CloudShell has two complementary models. The Resource model owns
the graph model: resource structure, declared relationships, resource-owned
attributes, capability declarations, operation declarations, and the
resolution rules that turn those declarations into usable capability and
operation behavior. Resource Manager owns the operational model: records of
existing resources and the state and behavior it needs to operate its Control
Plane domain. When Resource Manager needs graph knowledge or behavior, it
resolves the Resource model graph and composes that result with its own
operational resource record.

The Resource Manager API should therefore expose a projection composed from
both sources: the Resource Manager resource record and the resolved `Resource`
from the Resource model graph. This composition lets Resource Manager keep
operational data and behavior in its own model while still using Resource
model capabilities and operations when it needs to validate, plan, update, or
execute graph-aware behavior.

The current integration POC starts with that projection seam. A small bridge
adapter maps resolved Resource model `Resource` instances to the existing
Resource Manager-facing `CloudShell.Abstractions.ResourceManager.Resource`
shape and exposes them through `IResourceProvider`. This does not replace
Resource Manager storage or orchestration. It tests whether the new Resource
model can be consumed by the existing Resource Manager composition path before
the project decides which existing declaration/provider paths it should
replace. The bridge provider can be registered with the existing
`ResourceManagerStore` like any other provider, letting the current Resource
Manager registration filtering, metadata composition, resource class
projection, capability projection, and action projection run over resources
that originated in the new Resource model. The bridge can also resolve a
`ResourceGraphSnapshot` on demand through `ResourceResolver`, which keeps the
Resource Manager entry point stable while moving graph resolution to the
provider boundary where graph-aware behavior is needed.

A fuller integration should not make Resource Manager persist resolved
Resource model `Resource` projections directly. Resource Manager-facing
resources should be projections composed from resolved Resource model
resources, stripped Resource model state data such as `ResourceState`, and the
Control Plane's own operational records. The stripped Resource model state
data is the durable graph-owned container for identity, declared attributes,
capability and operation payloads, metadata, revisions, and timestamps. The
resolved `Resource` is then recomputed from that state plus
`ResourceTypeDefinition`, `ResourceClassDefinition`, provider declarations,
and the active resolution context. Resource Manager can add liveness,
authorization, grouping, procedures, logs, traces, runtime metadata, and other
operational facts on top without turning those responsibilities into Resource
model persistence concerns.
When the backing store wants a more compact or store-optimized shape, it can
persist `ResourceRecord` rows/documents and rehydrate them into `ResourceState`
at the graph boundary before resolution.

The bridge should also project Resource model diagnostics into Resource
Manager diagnostics. That makes invalid graph definitions visible through the
existing `GetResourceModelDiagnostics()` surface instead of hiding resolver
diagnostics inside the bridge provider. The initial POC maps
`ResourceDefinitionDiagnostic` entries to `ResourceModelDiagnostic` entries
with the Resource model diagnostic code and message preserved, while the
diagnostic source identifies the Resource model bridge.

The bridge should also provide a caller-owned graph access point for workflows
that need Resource model behavior. Resource Manager or an orchestrator can
resolve a graph resource by the shared resource ID, optionally include its
dependency closure, and receive the graph snapshot version plus the resolved
`Resource` projections with capability and operation work units bound. The
caller still owns any graph transaction, locking, apply dispatcher, retry, and
commit policy. This keeps capability and operation objects as integration
work units while leaving graph stability decisions at the Control Plane or
Resource Manager boundary.

For the POC, capability and operation work units should be designed to modify
only the `Resource` they are attached to when they produce Resource model
changes. They may still perform integration logic, such as asking Resource
Manager to run a procedure or querying other services, but any direct Resource
model mutation should be limited to the attached resource and returned as a
caller-owned change set. Cross-resource graph mutations, graph-wide
side-effects, graph-wide isolation, and whether a capability or operation must
run inside a transaction or other stable graph scope are deferred coordination
concerns for the Resource Manager, orchestrator, or Control Plane layer.

The same bridge can resolve a declared capability by capability ID and return
the capability projection registered by the consuming boundary. This gives
Resource Manager, Control Plane services, or an orchestrator a typed
capability work unit without making the bridge own the capability behavior.
If the capability is declared but no capability projection has been registered,
the bridge returns diagnostics so the caller can expose the capability as
unavailable, route to another implementation, or keep the workflow read-only.

The bridge can also translate a Resource Manager action request into the
matching Resource model operation projection by using the action ID as the
operation ID. That gives Resource Manager or an orchestrator a typed operation
work unit to inspect or execute without making the bridge the operation
executor. If the operation is declared but no operation projection has been
registered by the consuming boundary, the bridge returns diagnostics instead
of throwing, so the caller can expose the operation as unavailable or route it
to another implementation.

When a host wants direct Resource Manager action integration, it can register
an explicit procedure-capable bridge provider. That provider still lists the
same graph-backed Resource Manager resources, but it also implements Resource
Manager action availability and procedure execution by resolving the matching
Resource model operation projection. It only executes operations that opt into
the generic executable operation projection contract. This keeps the read-only
graph provider and the procedure-capable provider as separate host choices.
The helper should wire the same scoped bridge instance into both the
`IResourceProvider` and `IResourceActionAvailabilityProvider` collections so
standard Control Plane composition can discover availability reasons without
host-specific adapter code.
Graph-backed Resource Manager projections should also carry bridge-provider
metadata. The procedure-capable bridge must use that metadata when evaluating
actions, rather than claiming every declared resource with a matching action
ID, so it does not interfere with unrelated Resource Manager providers.
The Resource Manager bridge should use the same graph-reference resolution
rules as dependency-closure resolution when projecting provider-produced
dependencies. Missing staged dependencies may remain visible as declared
`DependsOn` IDs, but a reference that resolves to an existing resource of the
wrong expected type should be diagnosed and not projected as an actionable
Resource Manager dependency.
Reference resolution may still return the target resource for diagnostics and
debugging when an expected-type check fails, but the bridge should bind
capability and operation projections only for successfully resolved references.
That keeps invalid targets inspectable without making their behavior available
through the wrong relationship.
The procedure-capable bridge may also use those typed-reference diagnostics as
operation availability blockers. The current POC should keep that policy
narrow: wrong-type existing dependency targets are unsafe for runtime
execution, while broader dependency validation, missing staged resources, and
cross-resource orchestration policy remain provider/Resource Manager concerns
to refine as real providers are ported.

The bridge project should own registration helpers for this integration seam.
Hosts can register a graph-backed Resource model provider as an existing
Resource Manager `IResourceProvider` without making `CloudShell.ControlPlane`
reference the experimental Resource model infrastructure directly. The host
still owns which `ResourceResolver`, graph snapshot source, and graph model
services it registers.

The same integration helper should make `ResourceResolver` host-wirable from
registered Resource model providers. A host can register class definitions,
`IResourceTypeProvider` implementations, and attribute validators, then let the
bridge compose a resolver from those services. That keeps provider packages as
the owners of their type definitions while Resource Manager consumes the
resolved graph through the existing provider composition path.
Provider packages may register their own `ResourceClassDefinition` defaults
when they own the class boundary. Hosts can still register explicit class
definitions for shared or host-owned classes; when the same class id is
registered more than once, later registrations override earlier defaults for
resolver composition. Resource definition validation should use the same
override rule so graph resolution and validation do not disagree about which
class shape applies.

The expected migration path is to keep the bridge temporary and incremental.
Once the graph model, provider registration, resolution, diagnostics, and
Resource Manager projection path work well enough, existing resource providers
can be ported to the new provider model one boundary at a time. After the
ported providers cover the required Resource Manager behavior, the older
resource provider infrastructure can be removed instead of maintained as a
parallel long-term model.

Porting a provider means implementing the complete Resource model support that
the resource type needs to work, not only mapping an existing provider to a new
list or projection interface. A ported resource type should own its
`ResourceTypeDefinition`, attribute definitions and validation, supported
capability declarations and capability provider implementations, supported
operation declarations and operation provider implementations, plus any apply,
update, or provider-owned behavior required for Resource Manager and other
Control Plane consumers to use that type through the new model.

Provider registration should follow the same boundary. The reference POC uses
a singular executable application resource-type registration that wires the
type provider, capability providers, operation providers, projection provider,
and apply/change handlers needed by that type. It should not grow into a broad
application-provider aggregate that registers unrelated executable, project,
container, and database resource types behind one provider identity.

The POC should also include at least one second resource type from another
boundary. A local volume resource type is a useful test because it lets the
model prove that storage class/type defaults, apply validation, provider-owned
operation projection, typed wrappers, and Resource Manager projection compose
with executable application capabilities without folding storage behavior into
an application provider aggregate. Later, when an existing provider is ported,
the verification path should be to register the ported provider through the
new model, turn off the old registration, and prove the Resource Manager and
orchestration paths still work through the graph. The first acceptance test
for that path should apply a deployment containing both resource types,
project the committed graph through the Resource Manager bridge, resolve the
executable resource with its storage dependency from the graph, and execute a
Resource model operation through the bridge.

A narrow container application reference provider extends the same proof
without porting the full legacy container app provider. It owns
`application.container-app`, container image and replica attributes,
start/restart operation providers, a typed image-update operation provider,
and a typed projection wrapper. The image-update operation stages resource-local
attribute changes on the attached `Resource`; the container application type
provider then accepts or rejects those proposed changes through the normal
apply hook. The shared `storage.volumeConsumer` capability can attach to both
executable and container application resources through the capability provider
boundary, so volume behavior is not folded into either application provider
implementation. The capability provider should act on the resolved capability
declaration rather than maintaining a hard-coded list of compatible resource
types; type compatibility can be expressed by which resource types declare or
accept the capability and by graph-level validation.

An ASP.NET Core project reference provider extends the proof without depending
on `CloudShell.Providers.Applications`. It owns
`application.aspnet-core-project`, project path and launch-related attributes,
start/restart operation providers, a typed projection wrapper, and shared
volume-consumer capability support. Provider-specific services, configuration
records, operation implementations, validators, and projectors should live in
the provider boundary that owns them. The generic Resource model
infrastructure should only take abstractions that multiple providers prove are
shared: graph resolution, definition/state projection, capability and
operation contracts, diagnostics, and registration composition. Unique provider
behavior should not move into broad shared infrastructure just because it is
needed by the first provider that exercises a scenario. The reference POC
applies this by keeping provider-owned configuration records and operation
provider services in separate files next to the owning resource type provider,
while the type provider stays focused on definition shape, validation, and
apply planning. Each reference resource provider should live in its own
folder, with shared capability implementations placed in a dedicated shared
capability folder. That keeps type-specific constants, validators,
operations, projections, and service registration close to the provider
boundary that owns them.

A narrow configuration store reference provider extends the proof outside the
old application-provider group. It owns `configuration.store`, configuration
class defaults, endpoint and entry-count attributes, an inspect operation, a
typed projection wrapper, and Resource Manager bridge coverage. The POC keeps
the actual configuration entries out of the string-backed attribute state for
now; later typed/complex attribute values can represent the entry collection
or a provider-owned configuration payload without changing the provider
boundary.

A narrow host configuration source reference provider covers the companion
`configuration.host` type. It owns host-source defaults, source and
entry-count attributes, an inspect operation, a typed projection wrapper, and
Resource Manager bridge coverage. The POC records only the exposed-entry count
as Resource model state; actual host configuration values are read and
authorized by the provider/runtime layer.

A narrow Docker host reference provider covers the provider-specific
`docker.host` type. It owns Docker host kind, endpoint, registry, default-host
attributes, passive container image/build/filesystem-mount capability markers,
an inspect operation, a typed projection wrapper, and Resource Manager bridge
coverage. It stays separate from the generic `cloudshell.container-host`
reference provider so Docker-owned runtime services and attributes can evolve
inside the Docker provider boundary.

A narrow load balancer reference provider covers the declarative
`cloudshell.loadBalancer` graph resource. It owns provider, host-resource,
entrypoint-count, route-count, and endpoint-count attributes, passive
networking capability markers, an apply-configuration operation, a typed
projection wrapper, and Resource Manager bridge coverage. The POC records
summary counts only; route and entrypoint collections can move into typed
complex values or provider-owned configuration payloads in a later slice.

A narrow network reference provider covers the declarative `cloudshell.network`
graph resource. It owns network kind, host-readiness, and mapping-provider
attributes, passive networking capability markers, a reconcile-endpoint-
mapping operation, a typed projection wrapper, and Resource Manager bridge
coverage. The POC treats those attributes as resource-owned state or
configuration. Fetched or calculated network views, such as observed endpoint
or mapping summaries, should be exposed through resolved capability members or
operation plans rather than stored as normal resource attributes.

A narrow virtual network reference provider covers the more specific
`cloudshell.virtualNetwork` graph resource. It owns virtual-network kind,
default-network, host-readiness, and mapping-provider attributes, passive
virtual-network and ingress capability markers, a type-specific implementation
of the shared `reconcileEndpointMappings` operation ID, a typed projection
wrapper, apply planning, and Resource Manager bridge projection/execution.
Endpoint collections and observed endpoint mappings remain future typed
payloads or capability members rather than normal count attributes.

A narrow DNS Zone reference provider covers the declarative
`cloudshell.dnsZone` graph resource. It owns the DNS zone name and selected
DNS provider attributes, the passive DNS-zone capability marker, a reconcile
name-mappings operation, a typed projection wrapper, and Resource Manager
bridge coverage. The POC intentionally does not copy record-count, conflict,
or materialization-status attributes from the old platform provider because
those are derived or observed views that should be exposed through resolved
capability members or operation plans.

A narrow name-mapping reference provider covers the declarative
`cloudshell.nameMapping` graph resource. It owns host name, target endpoint,
and exposure attributes, the passive name-mapping capability marker, a typed
projection wrapper, apply planning, and Resource Manager bridge projection.
References to the DNS zone, target resource, or provider resource are declared
as `ResourceReference` entries in `DependsOn` for the POC instead of raw ID
attributes. Runtime status, conflict status, and DNS publishing observations
remain derived or observed views for future capability members or operation
plans.

A narrow storage reference provider covers the declarative `cloudshell.storage`
graph resource. It owns storage kind, provider, medium, and location
attributes, passive storage-provider and mount-provider capability markers, an
inspect operation, a typed projection wrapper, apply planning, and Resource
Manager bridge projection/execution. The POC keeps storage volume counts,
filesystem availability, and runtime status out of normal attributes because
those are calculated or observed views that should be exposed through
capability members or operation plans.

A narrow CloudShell volume reference provider covers the declarative
`cloudshell.volume` graph resource. It owns provider, medium, location,
subpath, access-mode, and persistence attributes, the passive storage-volume
capability marker, a type-specific implementation of the shared
`storage.volume.provision` operation ID, a typed projection wrapper, apply
planning, and Resource Manager bridge projection/execution. References to a
storage resource are expressed as `ResourceReference` dependencies in the POC
instead of the old raw `storage.volume.storageResourceId` attribute. Runtime
availability remains an observed view for future capability members or
operation plans.

A narrow service reference provider covers the declarative `cloudshell.service`
graph resource. It owns service kind and routing-mode attributes, the passive
endpoint-source capability marker, a reconcile operation, a typed projection
wrapper, apply planning, and Resource Manager bridge projection/execution.
Service targets and network relationships are expressed as `ResourceReference`
dependencies for the POC. Port, endpoint, and target collections remain future
typed payloads or capability members instead of normal count attributes.

A narrow Secrets Vault reference provider follows the same boundary while
keeping secret material out of the Resource model. It owns `secrets.vault`,
Secrets Vault class defaults, endpoint and secret-count attributes, an inspect
operation, a typed projection wrapper, and Resource Manager bridge coverage.
The POC intentionally stores only non-secret projected facts; secret values
and secret-value lifecycle behavior remain provider-owned runtime concerns.

A narrow identity provisioning reference provider covers
`cloudshell.identity-provisioning` as infrastructure that declares identity
provider setup intent without making identity runtime realization inherent to
the Resource model. It owns the `infrastructure.kind`, `identity.provider`,
and `identity.providerKind` attributes, a passive identity-provisioning
capability marker, a setup operation, a typed projection wrapper, apply
planning, and Resource Manager bridge projection/execution. Provider-native
clients, directory records, credential issuance, and grant reconciliation
remain provider-owned operational concerns for Resource Manager or Control
Plane integrations.

A narrow local host networking reference provider covers
`cloudshell.hostNetworking.local` as infrastructure that declares host network
mapping support for the graph. It owns `infrastructure.kind`,
`network.hostReadiness`, `host.os`, and `networking.mode` attributes, passive
networking provider, endpoint mapper, gateway, ingress, and host-network
capability markers, a type-specific endpoint-mapping reconcile operation, a
typed projection wrapper, apply planning, and Resource Manager bridge
projection/execution. Live mapping counts and host proxy runtime state remain
observed provider state for future capability members or operation plans, not
declared Resource graph attributes.

A narrow macOS host networking reference provider covers
`cloudshell.hostNetworking.macos` as the OS-specific sibling for host network
mapping support. It declares the same graph-level host networking shape as the
local provider while setting `host.os` to `macos`. Platform support checks,
host proxy runtime state, and resolver/provisioner integration remain
operational provider concerns outside the Resource model POC.

A narrow Docker container reference provider covers `docker.container` as a
provider-projected container artifact. It owns stable workload, image,
registry, replica, and endpoint-count attributes, passive monitoring and log
source capability markers, lifecycle operation projections, a typed wrapper,
apply planning, and Resource Manager bridge projection/execution. Provider
managed endpoint count updates remain outside the type provider itself; a
future provider integration boundary may delegate refresh/reconcile work that
updates this value in `ResourceState` while keeping it out of rendered
`ResourceDefinition` output. Actual Docker API calls, log streaming, runtime
discovery, and state-sensitive action availability remain operational provider
concerns.

The working porting status for the reference POC is:

| Provider or resource type | Status | New-model coverage | Remaining outside the POC |
| --- | --- | --- | --- |
| Executable application (`application.executable`) | Ported as a reference provider | Type and class defaults, executable path validation and configuration, shared volume-consumer capability, start operation, typed wrapper, Resource Manager bridge projection and execution | Real local-process runtime integration, logs, endpoints, templates, and UI registration/update flow |
| Local volume (`storage.volume`) | Ported as a reference provider | Storage class and type defaults, medium validation, provision operation, typed wrapper, apply planning, Resource Manager bridge projection | Provider-backed storage materialization, usage tracking, health, and monitoring |
| Storage (`cloudshell.storage`) | Ported as a narrow reference provider | Storage class/type defaults, provider/medium/location attributes, passive storage-provider and mount-provider capability markers, inspect operation, typed wrapper, apply planning, and Resource Manager bridge projection/execution | Volume collection payloads, runtime filesystem availability and volume counts as capability members or operation plans, provider-backed storage materialization, health, monitoring, and UI registration/update flow |
| CloudShell volume (`cloudshell.volume`) | Ported as a narrow reference provider | Storage class/type defaults, provider/medium/location/subpath/access-mode/persistence attributes, passive storage-volume capability marker, `ResourceReference` storage dependencies, type-specific `storage.volume.provision` operation provider, typed wrapper, apply planning, and Resource Manager bridge projection/execution | Storage-reference graph validation, runtime filesystem availability as capability members or operation plans, provider-backed volume materialization, health, monitoring, and UI registration/update flow |
| Service (`cloudshell.service`) | Ported as a narrow reference provider | Service class/type defaults, service kind/routing-mode attributes, passive endpoint-source capability marker, `ResourceReference` target/network dependencies, reconcile operation, typed wrapper, apply planning, and Resource Manager bridge projection/execution | Port, endpoint, target, and health-check payloads, endpoint projection through Resource Manager, target validation, orchestration integration, and UI registration/update flow |
| Container application (`application.container-app`) | Ported as a narrow reference provider | Image and replica attributes, shared volume-consumer capability, start/restart/image-update operations, typed wrapper, Resource Manager bridge projection and execution | Actual container host orchestration, endpoints, revisions, replica runtime state, monitoring, and UI operations |
| ASP.NET Core project (`application.aspnet-core-project`) | Ported as a narrow reference provider | Project path, arguments, hot reload, launch-settings attributes, shared volume-consumer capability, start/restart operations, typed wrapper, Resource Manager bridge projection and execution | Launch settings parsing, endpoints, local process or container build behavior, UI registration/update flow |
| SQL Server (`application.sql-server`) | Ported as a narrow reference provider | Service class and type defaults, version/edition attributes, declared database configuration, shared volume-consumer capability, reconcile-access operation, typed wrapper, Resource Manager bridge projection and execution | Real SQL runtime integration, credential/grant reconciliation, database child projections, endpoints, and UI tabs |
| SQL database child (`application.sql-database`) | Ported as a narrow reference provider | Database name/source/ensure-created attributes, server `ResourceReference` validation, ensure-created operation, typed wrapper, Resource Manager bridge projection and execution | Real SQL database materialization, credential/grant reconciliation, provider-managed child ownership metadata, and UI tabs |
| Container host (`cloudshell.container-host`) | Ported as a narrow reference provider | Infrastructure class/type defaults, host kind/endpoint/registry/default attributes, passive container image/build/filesystem-mount capability markers, inspect operation, typed wrapper, Resource Manager bridge projection and execution | Real Docker/container host runtime integration, host resolution, placement behavior, credentials, and runtime diagnostics |
| Docker host (`docker.host`) | Ported as a narrow reference provider | Infrastructure class/type defaults, Docker host kind/endpoint/registry/default attributes, passive container image/build/filesystem-mount capability markers, inspect operation, typed wrapper, Resource Manager bridge projection and execution | Real Docker runtime integration, discovery, health, logs, container child projections, credentials, and UI registration/update flow |
| Docker container (`docker.container`) | Ported as a narrow reference provider | Container class/type defaults, workload/image/registry/replica attributes, read-only endpoint-count attribute, passive monitoring and log-source capability markers, lifecycle operation projections, typed wrapper, apply planning, and Resource Manager bridge projection/execution | Real Docker API integration, runtime discovery, container state, state-sensitive action availability, log streaming, endpoint projection, and hidden/runtime-managed Resource Manager behavior |
| Load balancer (`cloudshell.loadBalancer`) | Ported as a narrow reference provider | Network class/type defaults, provider/host attributes, read-only count attributes, passive networking capability markers, apply-configuration operation, typed wrapper, Resource Manager bridge projection and execution | Route and entrypoint payloads, target `ResourceReference` graph validation, Traefik/materialization runtime integration, endpoint mappings, and UI registration/update flow |
| Network (`cloudshell.network`) | Ported as a narrow reference provider | Network class/type defaults, kind/readiness/provider attributes, passive networking capability markers, reconcile-endpoint-mappings operation, typed wrapper, Resource Manager bridge projection and execution | Endpoint and mapping payloads, observed mapping state as capability members, host/virtual network specialization, provisioner integration, and UI registration/update flow |
| Virtual network (`cloudshell.virtualNetwork`) | Ported as a narrow reference provider | Network class/type defaults, virtual/default/readiness/provider attributes, passive virtual-network and ingress capability markers, type-specific `reconcileEndpointMappings` operation provider, typed wrapper, apply planning, and Resource Manager bridge projection/execution | Endpoint and mapping payloads, observed mapping state as capability members or operation plans, endpoint mapping provisioner integration, and UI registration/update flow |
| Local host networking (`cloudshell.hostNetworking.local`) | Ported as a narrow reference provider | Infrastructure class/type defaults, host-readiness/OS/mode attributes, passive networking provider/endpoint-mapper/gateway/ingress/host-network capability markers, type-specific `reconcileEndpointMappings` operation provider, typed wrapper, apply planning, and Resource Manager bridge projection/execution | Live mapping counts, host proxy runtime state, endpoint mapping provisioner integration, macOS-specific provider specialization, diagnostics, and UI registration/update flow |
| macOS host networking (`cloudshell.hostNetworking.macos`) | Ported as a narrow reference provider | Infrastructure class/type defaults, host-readiness/OS/mode attributes, passive networking provider/endpoint-mapper/gateway/ingress/host-network capability markers, type-specific `reconcileEndpointMappings` operation provider, typed wrapper, apply planning, and Resource Manager bridge projection/execution | Platform support checks, live mapping counts, host proxy runtime state, endpoint mapping provisioner integration, diagnostics, and UI registration/update flow |
| DNS Zone (`cloudshell.dnsZone`) | Ported as a narrow reference provider | Network class/type defaults, zone/provider attributes, passive DNS-zone capability marker, reconcile-name-mappings operation, typed wrapper, Resource Manager bridge projection and execution | Name-mapping child resource integration, record/conflict/materialization views as capability members or operation plans, DNS publisher integration, and UI registration/update flow |
| Name mapping (`cloudshell.nameMapping`) | Ported as a narrow reference provider | Network class/type defaults, host/endpoint/exposure attributes, passive name-mapping capability marker, `ResourceReference` dependencies, typed wrapper, apply planning, and Resource Manager bridge projection | Typed reference attributes when complex attribute values are promoted, target endpoint validation, conflict/materialization views as capability members or operation plans, DNS publisher integration, and UI registration/update flow |
| Configuration store (`configuration.store`) | Ported as a narrow reference provider | Configuration class/type defaults, endpoint and read-only entry-count attributes, inspect operation, typed wrapper, Resource Manager bridge projection and execution | Real configuration service runtime integration, entry collection payloads, authorization, logs, templates, and UI registration/update flow |
| Host configuration source (`configuration.host`) | Ported as a narrow reference provider | Configuration class/type defaults, source and read-only entry-count attributes, inspect operation, typed wrapper, Resource Manager bridge projection and execution | Runtime host configuration lookup, entry-name payloads, authorization, templates, and UI registration/update flow |
| Secrets Vault (`secrets.vault`) | Ported as a narrow reference provider | Secrets Vault class/type defaults, endpoint and read-only secret-count attributes, inspect operation, typed wrapper, Resource Manager bridge projection and execution without storing secret values | Real Secrets Vault runtime integration, secret collection payloads, authorization, logs, templates, and UI registration/update flow |
| Identity provisioning (`cloudshell.identity-provisioning`) | Ported as a narrow reference provider | Infrastructure class/type defaults, provider/provider-kind attributes, passive identity-provisioning capability marker, setup operation, typed wrapper, apply planning, and Resource Manager bridge projection/execution | Real identity provider setup, directory/client materialization, credential issuance, grant reconciliation, authorization, diagnostics, and UI registration/update flow |

Host infrastructure registration is a separate concern from provider
registration. A host may compose the generic graph services once from whatever
class definitions, type providers, validators, capability providers, operation
providers, projection providers, and apply/change handlers are registered. The
generic graph-service helper should not own provider identity or decide which
resource types belong together; each resource type provider package keeps that
boundary.

For the integration POC, hosts can also register an in-memory Resource model
graph and expose it through the existing Resource Manager provider composition
path. That keeps the bridge close to the eventual server shape: Resource
Manager consumes a graph model service, while provider packages separately
register resource-type behavior and the host decides which graph store backs
the model.

This migration does not mean Resource Manager stops owning resources in its
Control Plane domain. Resource Manager can continue to keep operational
resource records and project from its own data, from the resolved Resource
model graph, or from both when a workflow needs graph-aware information. The
Resource model becomes the declaration and graph-behavior boundary; Resource
Manager remains the operational entry point that composes the view it needs.

Graph locking, graph update coordination, and transaction policy belong at
the Resource Manager or Control Plane coordination layer. The Resource model
can provide change sets, resolved projections, diagnostics, and commit-shaped
results, but it should not own the policy for whether Resource Manager locks
the graph, uses optimistic transactions, retries, merges, or rejects
concurrent updates.

A future orchestrator or lifecycle service can follow the same boundary. It
can start from the Control Plane's own Resource Manager resource record, use
the shared resource ID to resolve the matching Resource model graph node and
any dependencies it needs, then use the resolved graph knowledge,
capabilities, and operations while operating the lifecycle. If that operation
needs to update declared graph state, it should persist only the necessary
Resource model changes. Any locks, transactions, retries, or conflict handling
between reading the Control Plane resource, resolving the graph, executing
provider behavior, and committing graph changes remain implementation details
of the Resource Manager, orchestrator, or broader Control Plane coordination
layer.

The POC supports that lookup shape with a small Resource model graph resolver
that can resolve a target resource and its dependency closure from a
`ResourceGraphSnapshot`. Explicit `DependsOn` entries are `ResourceReference`
objects, not raw resource ID strings. A reference carries the target value, the
relationship, and the addressing mode. The current resolver only resolves
`dependsOn` references addressed by `resourceId`, but the document shape can
later represent references to projected resources or provider-native
addresses. `ResourceReference` is the primitive for saying that one model
element references a resource. A plain resource ID is only one addressing value
inside that primitive, not the relationship model itself. `DependsOn` is the
graph-level collection of `ResourceReference` values. A typed resource
attribute may also carry a `ResourceReference` when the relationship belongs
to the resource's own shape. For example, the former
`database.serverResourceId` string attribute should become `database.server`,
with a complex `ResourceReference` value, rather than duplicating the target
identity as a plain string. The POC currently keeps attributes string-backed,
so this belongs with the broader typed/complex attribute-value work.
References may also carry optional expectations, such as expected resource
type or provider id, so validation can reject a reference that resolves to the
wrong kind of target. The current POC resolves `resourceId` references with an
expected resource type and emits a graph diagnostic when the target exists but
has a different `ResourceTypeId`. A later proposal should define additional
reference arguments for projected-resource and provider-native addressing
modes, for example selector arguments, projection names, provider scopes, or
other addressing hints. That should remain part of the reference model instead
of being copied into unrelated resource attributes.

A future `ResourceDefinition` attribute value for a graph reference can use a
shape like:

```json
{
  "attributes": {
    "database.server": {
      "refType": "graph",
      "resourceId": "application.sql-server:server",
      "resourceTypeId": "application.sql-server"
    }
  }
}
```

`refType` identifies the reference addressing family. A missing or `graph`
value means a normal graph reference. Later alternatives can represent
resources that are not in the graph and need projection. `resourceTypeId` is
optional expectation metadata: the attribute definition on the
`ResourceTypeDefinition` or `ResourceClassDefinition` may constrain the
expected type, while the resource value may also declare an expected type when
the definition leaves it unconstrained and the author knows the target shape.
Validation can then diagnose a `ResourceReference` attribute that resolves to
the wrong resource type. For graph-level `DependsOn` references, the POC
already supports the same expected-type check through `ResourceReference.TypeId`.
Provider-produced dependencies should set this expectation when the provider
knows the required target shape, so graph resolution can reject a dependency
that points at an existing resource of the wrong type instead of silently
following it. When a provider-produced typed reference targets the same
resource ID as an older untyped dependency declaration, the typed reference
should refine the relationship for resolution so transition-era declarations
do not bypass provider-owned validation.
Resolving a `ResourceReference` is a first-class graph operation:
when the reference can be resolved, the result carries the projected
`Resource`; when it cannot, the result can stay unresolved or carry
diagnostics. Direct lookup by resource ID remains supported for consumers that
already have a graph resource address and only need the corresponding
projection. The dependency-closure result also exposes the reference
resolutions it followed, so consumers can distinguish the declared relationship
from the target resource projection. The Resource Manager bridge can resolve a
`ResourceReference` directly and binds capability and operation projections on
the target when the reference resolves to a graph resource. Direct bridge
lookup by resource ID delegates to the same core graph resolver, so missing
resource diagnostics and graph lookup behavior stay consistent. It also
preserves followed reference resolutions when resolving dependency closures
for Control Plane consumers. The closure includes dependency references contributed by
registered `IResourceGraphDependencyProvider` implementations. Those providers
return `ResourceReference` objects too, and the current resolver applies the
same `dependsOn` plus `resourceId` filter before following them. This lets
capability-owned relationships, such as mounted volumes, participate in graph
traversal without forcing every authoring path to duplicate those references
into `DependsOn`. Resource Manager graph resource projection also includes
those provider-derived dependency references in the projected `DependsOn` list
when they resolve through the current resource-id addressing mode. The resolver
returns resolved `Resource` projections plus diagnostics for missing graph
nodes or dependency cycles; it does not decide lifecycle ordering, lock policy,
or persistence behavior.

Identity and authorization hooks are Resource model and graph concerns, but
identity should not automatically become an inherent property of the core
`Resource` type. The graph can declare principal or identity-related data as a
dedicated interchange field, such as `principal`, or as a resource-owned
attribute, such as `attributes.principal`. If attributes need to carry that
kind of data, the attribute model may need to support scalar values and
structured object values. An identity capability can then expose attached
methods and properties that interpret those declared values. The realization
of the identity, credential materialization, policy enforcement, and runtime
authorization checks can remain part of the operational model owned by
Resource Manager or the broader Control Plane.

The distinction should be kept explicit:

| Concept | Describes | Owned by |
| --- | --- | --- |
| `Resource` | Low-level resolved resource projection from attributes, capabilities, operations, presets, and actual state | Control Plane projection over provider state |
| `ResourceType` | Resolved view of a static `ResourceTypeDefinition` | Resource type resolver/provider boundary |
| `ResourceClass` | Resolved view of a static `ResourceClassDefinition` | Resource class resolver/provider boundary |
| `ResourceDefinition` | Interchange model/format rendering of a `Resource` and input format for applying changes | Control Plane plus owning resource type provider |
| `ResourceTypeDefinition` | Static type-level declaration for shape, presets, capability support, operation support, and validation expectations | Owning resource type provider |
| `ResourceClassDefinition` | Static class-level declaration for shape, presets, shared expectations, and cross-type contracts | Platform or class-owning provider package |
| `Capability` | Resolved capability entry on a `Resource` | Capability contract owner |
| `Operation` | Resolved operation entry on a `Resource` | Operation contract owner |
| capability provider | Behavior implementation that acts on a `Resource` for a resolved capability | Capability provider package |
| operation provider | Behavior implementation that acts on a `Resource` for a resolved operation | Operation provider package |
| typed resource wrapper | Higher-level source-generated facade over the low-level `Resource` projection | Resource definition tooling and provider contracts |
| Resource Manager resource | Managed operational resource with lifecycle, liveness, authorization, and runtime responsibilities that consumes the low-level projection | Resource Manager |
| definition configuration | Provider-owned desired configuration | Owning resource type provider |
| capability intent | Cross-cutting desired behavior attached to a definition | Capability provider |
| projected attributes | Stable non-secret facts about the current projection | Owning provider or Control Plane overlay |
| runtime state | Observed provider/runtime facts | Provider, orchestrator, or Control Plane operational store |

## Resource Providers

CloudShell can use `ResourceProvider` as the general term for classes that
provide or list projected `Resource` instances from any source. A resource
provider may project persisted resource state, apply a `ResourceDefinition`
interchange input, list provider-observed runtime artifacts, surface
diagnostics or child resources, or combine several source records into the
current resource graph.

For example:

```csharp
IReadOnlyList<Resource> resources =
    await containerProvider.GetResourcesAsync(cancellationToken);
```

In this terminology, a `ResourceProvider` answers "what resources are visible
now?" A resource type provider answers "how is this precise resource type
validated, accepted, changed, and projected?" Some provider classes may
implement both roles, but the names should keep the resource projection role
distinct from interchange rendering and apply behavior.

The exact `IResourceProvider` contract is intentionally open. It may be only a
resource resolution/listing surface, or it may include richer resolution by ID,
query, environment, graph, or projection context. It should be able to use
accepted resource definitions, provider-observed state, generated wrappers,
capability resolvers, and operation resolvers to project the resources it
returns.

Mutation is a separate question. Adding, updating, deleting, applying, or
tearing down resources may belong in focused contracts such as resource type
apply providers, definition stores, lifecycle providers, or operation
providers instead of on `IResourceProvider` itself. The POC should avoid
collapsing listing/resolution and mutation into one broad provider interface
until there is evidence that a combined contract is the right boundary.

## Capabilities vs Operations

Capabilities and operations both add behavior to the resource model, but they
serve different purposes.

They should not be treated as the persisted state container. Resource,
resource type, and resource class declarations say which capabilities and
operations exist for a resource. Resolution merges those declarations into
the effective `Resource.Capabilities` and `Resource.Operations` collections.
Providers attach behavior to those resolved entries and may return
diagnostics, projections, operations, or changes that modify the graph.

A capability describes functionality, role, or semantics attached to a
resource. Capabilities are commonly used by Resource Manager, Control Plane
services, providers, API projection, deployment projection, validation, and
selector logic. A capability may expose typed helper behavior, projected data,
requirements, or compatibility rules. It is not necessarily something a caller
invokes. Capability declarations are resolved into `.Capabilities` through
the same inheritance path as attributes and operations.

A capability declaration has two related uses. In the simplest form it is a
marker that says the resource supports a named capability. The owning resource
type provider may then handle that capability when it validates resource
state, applies changes, plans materialization, or projects Resource Manager
state. In the richer form, the same declaration can also be resolved through a
capability provider that attaches behavior to the projected `Resource`, such
as typed methods or properties exposed by a capability projection. The model
should therefore not require every capability declaration to have an attached
behavior provider.

Capability declaration and capability implementation should also stay
separate. A `ResourceClassDefinition` can declare that all resources in the
class have a capability and a provider package can register a default
implementation for that class-level capability. A more specific
`ResourceTypeDefinition` may need to override the implementation for one type
while keeping the same capability ID and contract. The resolver should allow a
type-specific capability provider to take precedence over a class-level
default, and it should also allow the type provider to opt back into the base
implementation when the inherited behavior is sufficient. This mirrors the
operation-provider rule: the declaration identifies the capability surface,
while provider resolution selects the implementation that best matches the
resolved resource.

Examples:

- `storage.volumeConsumer`: the resource can consume mounted volumes.
- `logs.sources`: the resource contributes log sources.
- `monitoring`: the resource contributes monitoring data.
- `networking.namePublisher`: the resource can publish names.

An operation is explicitly declared behavior on a resource class, resource
type, or individual resource state. It describes work that can be
invoked, applied, reconciled, or otherwise carried out for a resource. When an
operation is exposed to a caller, Resource Manager or the API can project a
command affordance for that operation. The operation itself remains the
domain-level behavior declaration. Operation declarations are resolved into
`.Operations`; the provider implementation for that declaration may vary by
resource class, resource type, provider, or resource instance.

An operation declaration names an operation surface on a resource. It is closer
to an interface method or Web API endpoint declaration than to the execution
implementation itself. A caller can discover that the resource has a named
operation, check whether it can execute, and invoke the matching operation
projection when one is registered. The implementation may live in an operation
provider, in the resource type provider boundary, or in a higher Control Plane
integration that maps the operation ID to its own behavior.

Examples:

- `start`: start the resource using the appropriate provider behavior.
- `restart`: restart or reconcile the running resource.
- `deployImage`: apply a new container image to a container application.
- `reconcileDatabaseAccess`: reconcile declared database grants.

The same operation ID can have different implementations for different
resource types or providers. A `start` operation for a local executable may run
a local process; a `start` operation for a container app may apply deployment
state and materialize containers; a provider-backed service may call an
external platform. This is why operation declarations and operation providers
should stay separate.

Capability providers usually answer "what functionality does this resource
have, require, or project?" Operation providers usually answer "how is this
declared behavior validated, projected, invoked, or applied for this
resource?" Both should receive a resolved resource context so they can see
inherited attributes, capabilities, operations, presets, provider defaults,
and diagnostics.

## Class and Type Definitions

Resources should be resolved against two inherited definition layers:

- `ResourceClassDefinition` describes broad expectations for a class such as
  executable, container, storage, network, configuration, service, or
  infrastructure.
- `ResourceTypeDefinition` describes precise type expectations such as
  `application.executable`, `application.container-app`, `cloudshell.volume`,
  or `cloudshell.storage`.

A resource instance then supplies concrete declared values. Conceptually:

```text
ResourceClassDefinition
    -> ResourceTypeDefinition
        -> Resource
            -> resolved Resource projection
```

Class and type definitions can contribute:

- default attributes
- required attributes
- attribute definitions and validators
- supported capabilities
- required capabilities
- default capability payloads
- supported operations
- operation requirements
- operation override policy
- provider selection requirements
- presets or named partial definition overlays
- class/type-level diagnostics and compatibility rules

`ResourceAttributeDefinition` is the contract-level place for attribute shape
metadata on `ResourceClassDefinition` and `ResourceTypeDefinition`. In the POC
class and type definitions carry attributes as a map keyed by
`ResourceAttributeId`, where each value is a `ResourceAttributeDefinition`:

```json
{
  "attributes": {
    "container:replicas": {
      "defaultValue": 1,
      "required": false,
      "readOnly": false,
      "mutability": "callerManaged"
    }
  }
}
```

`ResourceDefinition` keeps a different meaning for `attributes`: it is the
resource-owned state or interchange value map, keyed by attribute ID directly:

```json
{
  "attributes": {
    "container:replicas": 1
  }
}
```

The attribute definition carries an optional default value,
required-attribute intent, optional read-only intent, optional mutability
intent, an optional required message, a description, and an optional
serializer-neutral
`ResourceAttributeValueShape`. Those definitions participate in normal
resource resolution: class defaults are applied first, type defaults refine
them, and resource-owned state still wins when the attribute is writable.
Custom validation rules remain provider or platform validator hooks over the
resolved `Resource`; the attribute definition is not intended to become a full
provider configuration schema.

`ReadOnly` should describe whether callers may set or change an attribute
through authored `ResourceDefinition` input or graph change application.
`Mutability` should describe which boundary owns normal updates to the value.
The preferred shape is:

```csharp
new ResourceAttributeDefinition(
    ReadOnly: true,
    Mutability: ResourceAttributeMutability.ProviderManaged)
```

`ResourceAttributeMutability.ProviderManaged` is useful for
provider-projected facts such as observed endpoint counts, runtime state
summaries, generated identities, provider-native addresses, or other values
that should be visible on the resolved resource but not accepted as user-owned
desired state. A provider-managed read-only attribute can still have a default
or be projected by a provider into `ResourceState`; the important rule is that
apply/change validation should reject explicit caller attempts to create or
update it unless the operation runs in a trusted provider-owned projection
path. `ResourceDefinition` rendering should omit provider-managed read-only
attributes because the definition is an interchange and authoring surface, not
the provider state persistence surface. This keeps provider-managed state as
model metadata without tying the definition contract to JSON, YAML, XML,
database records, or any other serialization target.

`ReadOnly` and `Mutability` are intentionally related but not identical:
read-only is the caller access policy, while mutability is the ownership model
for how values are produced. The POC currently records
`ResourceAttributeMutability` on attribute definitions and resolved attribute
values, while caller enforcement still flows through `ReadOnly`. Provider-owned
refresh or apply-result paths use the mutability metadata to state why a value
may be updated by a provider but not authored by a caller. When a provider
returns accepted state that changes a read-only attribute, the change is valid
only if the effective attribute mutability is `ProviderManaged`; otherwise the
apply result should be rejected as a provider boundary violation. Accepted
provider-managed state must still be omitted when rendered back to
`ResourceDefinition`.

The Resource model graph should stay focused on declarative shape, declared
state, resolution, and commit boundaries. It should not own continuous runtime
processes that poll or watch external systems. Provider-managed attributes may
be updated later through explicit integration points owned by Control
Plane/ResourceManager/provider infrastructure, such as refresh, reconcile,
operation execution, or capability behavior. A type provider may declare that
an attribute is provider-managed and may delegate to those integration
services, but it should not become the long-running runtime monitor itself.
The provider boundary can still receive injected services when it is the
right integration point; the important POC constraint is that recurring tasks,
watchers, polling loops, and runtime reconciliation processes stay outside the
type provider contract until we have a concrete execution model for them.
When inherited definitions are resolved, an unset read-only value should
inherit the class-level policy. A type-level `false` should be treated as an
explicit definition-level decision to clear inherited read-only behavior, not
as the default behavior of every type attribute declaration.

Attribute definition overrides are a future model extension, not an immediate
POC priority. A later version may let `ResourceTypeDefinition` explicitly
override or refine an attribute declaration inherited from
`ResourceClassDefinition`, for example to replace a default value, narrow
validation, or bind a read-only class attribute to a concrete type-specific
value. That should be marked intentionally, such as with an override flag or
override policy, so accidental shadowing is rejected by validation. Read-only
would still apply to caller-authored resource state; class/type definition
overrides would be definition-level model composition, not normal resource
state mutation.

When a class or type definition declares both `defaultValue` and
`valueShape`, the resolver should validate that the default value matches the
shape before treating the resolved resource as valid. This includes checking
scalar kinds, object fields, required object fields, and array element shape.
That validation proves the definition contract is internally coherent; it does
not replace provider-owned validation of resource state or behavior.

The current POC still keeps resolved attribute values and defaults
string-based to avoid prematurely building the full value system. The intended
model should support scalar and complex attribute values, including structured
object and collection values that can be rendered as JSON objects, YAML
mappings, XML elements, or other document targets. `ResourceClassDefinition`
and `ResourceTypeDefinition` should therefore describe attribute value shape in
serializer-neutral CloudShell terms such as value kind, object fields, and
array element shape, not by embedding `JsonElement` or another format-specific
DOM as the definition contract. Format adapters can map the value object and
shape descriptors to JSON, YAML, XML, database records, or compact persistence
records at the boundary.

Stable IDs should use `:` as the namespace separator and `.` for local
hierarchy inside that namespace. For example, `container:replicas`,
`application:executable.path`, and `identity:principal.subject` have clear
owners while still leaving room for configuration-style sections. Canonical
resource model documents should keep the full ID as the map key so IDs remain
unambiguous across JSON, YAML, XML, database records, and in-memory maps.
Format-specific authoring adapters may render the namespace or dotted suffix
as nested sections when that improves readability, but that is a document
projection choice rather than the core model identity.

The resource instance supplies values, selects presets where allowed, and can
override values only within the constraints defined by the class and type. A
type definition should not be a passive label; it should be the contract that
explains what the resource must contain before the provider can accept it.
Operations can be declared at any of the three levels: class, type, or
resource instance. For example, `start` can be a class-level executable
operation, `deployImage` can be a type-level container-app operation, and
`reconcileDatabaseAccess` can be a resource-level operation exposed
only when a definition declares the relevant database capability or provider
configuration. A caller-facing command can then be projected from the resolved
operation declaration.

Those are the operation declaration sites. Operation providers do not declare
operations on their own; they advertise which resolved operation declarations
they can handle for matching resources.

Capability overrides should be explicit as well. A type definition can refine
or replace the provider implementation for a class-level capability without
renaming the capability. The selected implementation must still satisfy the
same capability contract unless the type also declares a more specific
capability ID. This keeps capability discovery stable while allowing concrete
resource types to supply better behavior than the class default.

Operation overrides should be explicit. A type definition can refine or hide a
class-level operation, and resource-owned state can refine or disable a
type-level operation only when the inherited definition allows that override.
This avoids accidental replacement of lifecycle behavior while still allowing
resource-specific provider operations.

Presets should be modeled as named overlays rather than hidden provider
shortcuts. A preset can provide default configuration, attributes,
capabilities, and operation policy, but it still resolves through the same
class and type validators. This keeps a preset reviewable and avoids a second
path that bypasses the resource-definition model.

## Resolution

Callers should avoid reading raw `.Attributes`, `.Capabilities`, or
`.Operations` when they need the effective model. Those members can be missing
inherited values, can contain invalid authored values, or can represent
provider projection rather than accepted intent.

Operations should follow the same resolution path as attributes and
capabilities. The raw collection records what was declared or observed at one
layer. The resolved collection is the effective view after class definitions,
type definitions, resource-owned state, presets, provider defaults, overrides,
and validators have been applied.

The exact API is still open, but the model needs supported methods or services
that can answer questions such as:

```csharp
Resource resource = resolver.Resolve(
    state,
    new ResourceDefinitionResolutionContext(environmentId, principal));

string? executablePath = resource.Attributes.GetString(
    ResourceAttributeNames.ExecutablePath);

bool consumesVolumes = resource.Capabilities.Has(
    ResourceCapabilityIds.StorageVolumeConsumer);

bool hasStartOperation = resource.Operations.Has(ResourceOperationIds.Start);
```

A resolved resource should expose effective values, type/class views, and
diagnostics:

```csharp
public sealed record Resource(
    ResourceState State,
    ResourceClass Class,
    ResourceType Type,
    ResourceAttributeSet Attributes,
    ResourceCapabilitySet Capabilities,
    ResourceOperationSet Operations,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics);
```

Effective values should carry source information. A resolved operation should
know whether it came from the class definition, type definition, resource
state, or a preset overlay, and it should record whether a lower level
overrode or disabled an inherited operation. That lets operation providers make
deliberate decisions about which operation declaration they are handling.

For example:

```csharp
ResourceOperationResolution startOperation = resource.Operations.Resolve(
    ResourceOperationIds.Start,
    ResourceOperationResolutionLevel.Type);

if (startOperation.IsAvailable)
{
    await operationProvider.ExecuteAsync(resource, startOperation, context);
}
```

The exact names are speculative, but the provider should be able to resolve a
matching declared operation at a specified level for the matching resolved
resource. It should not have to rediscover inheritance, presets, or override
rules locally.

The important requirement is not this exact API shape. The requirement is that
CloudShell has a deliberate resolution boundary that combines class
definitions, type definitions, resource-owned state, presets, provider
defaults, and provider observations before validation, projection, operation
availability, deployment projection, or UI rendering relies on those values.

Provider-facing resource contracts should act on `Resource`, not on
`ResourceDefinition`. The current POC now resolves directly to `Resource`
from `ResourceState`, `ResourceType`, and `ResourceClass`; it no longer needs
a separate `ResolvedResourceDefinition` model. The low-level `Resource`
combines persisted or accepted resource state, resolved type/class values,
resolved capabilities, and resolved operations. The important rule is that
capability providers, operation providers, attribute validators, and resource
type providers receive the resolved `Resource` context they need instead of
manually combining raw properties or re-reading the interchange format.

The current POC treats `ResourceDefinition` as the interchange model/format
and adds runtime projection on top of the resolved values. The first
projection layer is a low-level `Resource` exposing effective attributes,
capabilities, operations, type definition, and class definition. A second,
resource-type-specific wrapper can then provide an
object-oriented surface such as
`ExecutableApplicationResource.GetVolumesAsync()`. That method is owned by the
executable resource projection, but it internally asks
`Resource.Capabilities.Get<VolumeConsumerCapability>()` for the matching
volume capability behavior.

In this shape, capability behavior is not stored on the definition itself and
is not directly projected as generated wrapper methods. Capability providers
return projected capability work units bound to the resolved `Resource`.
Generated wrappers can compose those work units into their public surface.
Those behaviors can read effective resource values, resolve other
dependencies, project additional information, or return a change that can be
rendered as or applied from a `ResourceDefinition` when the capability changes
accepted state. Callers should not need to pass the resource definition back
into a projected capability; the capability already knows which `Resource` it
belongs to.

Operations should use the same structure. A `ResourceOperationResolver`
resolves an operation projection for a resolved `Resource` and operation ID.
The projected operation is a work unit bound to the resource and exposes the
operation's resolved definition, methods, properties, availability, and
diagnostics. The resolved operation can expose async methods such as
`CanExecuteAsync()` and `ExecuteAsync(...)` because availability and execution
may require provider state, authorization, dependencies, or runtime checks.
Generated wrappers can expose operation methods such as
`GetStartOperationAsync()` or a future source-generated convenience method
while keeping the resolved `Resource` as the state source.

For example:

```csharp
var volumeCapability = resource.Capabilities.Get<VolumeConsumerCapability>();
var mounts = volumeCapability.Mounts;
var volumeChanges = volumeCapability.AddMount(new("volume:logs", "Logs"));
var incrementalDefinition = volumeChanges.ToIncrementalDefinition();

var start = resource.Operations.Get<ExecutableStartOperation>();
if (await start.CanExecuteAsync(cancellationToken))
{
    await start.ExecuteAsync(cancellationToken);
}
```

Changes made through `Resource`, capability projections, operation
projections, or generated wrappers should be tracked as pending resource-state
changes until an explicit apply/commit boundary. The low-level resource view
can support direct staging:

```csharp
resource.SetAttribute(NamedAttributeIds.ContainerReplicasAttributeId, 2);

ResourceChangeSet changes = resource.ApplyChanges();
ResourceDefinition fullDefinition = changes.ToDefinition();
ResourceDefinition incrementalChange = changes.ToIncrementalDefinition();
```

`ApplyChanges()` in this model does not mean the graph has been mutated or
that provider/runtime consequences have cascaded. It creates an explicit
change set that can be validated, planned, accepted, rejected, persisted, or
projected as a full or incremental `ResourceDefinition`. A provider or future
resource manager commit pipeline owns the actual application of those changes
to resource state and the surrounding resource graph.

When an existing `ResourceState` applies a `ResourceDefinition`, the
definition is treated as an interchange overlay, not as a replacement for the
whole persisted state record. Attribute, configuration, capability, operation,
and metadata entries merge into the current resource-owned state so
incremental definitions do not drop unchanged values. Persistence metadata
such as resource revision and creation/last-modified timestamps stays on the
state object until the graph commit boundary accepts changes and assigns the
next committed values. Fresh imports can still create a new `ResourceState`
from a definition when there is no existing resource state to preserve.
The projected `Resource` can also turn an incoming `ResourceDefinition` overlay
into a `ResourceChangeSet`; that change set carries the proposed state plus the
attribute and capability diffs that provider apply hooks and graph commit
summaries need.
If the interchange definition targets a different resource identity or type,
the projected resource returns diagnostics instead of producing a change set
that would silently move state to another graph node.

The current POC adds that first provider-owned boundary through
`IResourceChangeApplyProvider` and `ResourceChangeApplyDispatcher`. The
dispatcher resolves the provider for the resource type in the
`ResourceChangeSet`, and the provider returns a `ResourceChangeApplyResult`
with diagnostics and an accepted `ResourceState` when the change is allowed.
The apply context can request commit behavior, but committing accepted state to
durable persistence or cascading changes through the graph remains a Resource
Manager/persistence concern outside this low-level projection model.
`ResourceDefinitionGraphChangeApplier` lifts that behavior to a graph
snapshot: it resolves each incoming `ResourceDefinition` overlay against the
current `ResourceState`, builds resource-local changes, runs the type-owned
apply providers, and returns one `ResourceGraphChangeSet` for the caller to
commit or reject. Missing target resources remain graph-level diagnostics
instead of being hidden inside a resource-local provider result.
Before staging resource-local changes, the graph applier now preflights the
incoming batch against the current snapshot and rejects duplicate incoming
resource IDs or dependencies that cannot be found in either the current graph
or the incoming definitions. Those remain graph-level diagnostics because the
individual resource type provider should not have to reason about whether the
whole definition batch is structurally coherent.
After resource-local apply providers accept their proposed state, the graph
applier can run registered `IResourceDefinitionGraphValidator` implementations
against the proposed graph before commit. This is the right place for
cross-resource capability references, compatibility checks, and other graph
rules that need resolved resources from more than one provider boundary. The
capability projection itself remains resource-local; the caller still owns the
graph scope and commit boundary.
When it resolves existing resources or create-missing definitions, the graph
applier also maps the apply context into `ResourceDefinitionResolutionContext`
so attribute validators can use the same environment and principal as the
type-owned apply provider.
When the caller is applying a deployment document rather than a conservative
overlay update, the applier can be explicitly told to create missing
resources. In that mode the incoming definition is resolved as a new
`Resource`, represented as a new-resource `ResourceChangeSet`, passed through
the same type-owned apply providers, and committed through the graph boundary.

`ResourceModelGraphDefinitionApplyService` is the current Resource
Manager-facing bridge for that flow: it loads the latest graph snapshot,
applies incoming interchange definitions through the graph applier, and
commits the resulting change set through `ResourceGraphModel`. The service
returns both the staged changes and commit result so Control Plane code can
inspect provider diagnostics, commit summaries, and version conflicts without
making the low-level resource model own Resource Manager policy.
Applying a `ResourceDeploymentDefinition` through the bridge opts into
creating missing resources because deployment documents describe desired graph
state, while direct definition-overlay application keeps creation disabled
unless the caller requests it.

Several accepted resource changes should also be committed as one graph
version. The POC models that with `ResourceGraphChangeTracker`,
`ResourceGraphChangeSet`, `ResourceGraphVersion`, and `IResourceStateProvider`.
The tracker groups accepted `ResourceChangeApplyResult` instances against a
base graph snapshot, and the state provider commits the batch or rejects it as
a unit. This is deliberately similar to an EF Core-style change graph: callers
can inspect the pending graph changes, persist full state, or persist only the
incremental definitions depending on the chosen persistence provider.
Individual resource changes never persist on their own; they are staged into a
resource graph change set and only the resource graph commit boundary writes
to the backing store.

The first persistence proof is `InMemoryResourceStateProvider`. It
materializes `ResourceState` objects from an in-memory store, accepts a
`ResourceGraphChangeSet`, checks the base graph version, applies all accepted
states, and increments the graph version once for the whole commit.
`InMemoryResourceRecordStateProvider` proves the same boundary with
store-optimized `ResourceRecord` data: records are rehydrated into
`ResourceState` for graph resolution and committed changes are stored back as
records. A database provider would use the same boundary, but materialize
resources from database records and persist the accepted state or
provider-specific delta format in a transaction.
The POC now extracts that record mapping into `IResourceGraphStoreProjector<TRecord>`
and `InMemoryProjectedResourceStateProvider<TRecord>` so a store-owned record
shape can hydrate graph state and write accepted graph payloads back without
making the Resource model own the whole store record.

The graph version is the batch/concurrency token for the whole resource graph.
Each persisted resource state also carries its own resource revision through
the serialized `Version` field, surfaced in the model as `ResourceRevision`.
When the state provider commits accepted changes, only changed resources get
their revision advanced and their last-modified timestamp updated. Creation
time is set when committed state is first persisted and preserved on later
commits. The projected `Resource` exposes `Version`, `Revision`, `CreatedAt`,
and `LastModifiedAt` from committed `ResourceState`; pending
`ResourceChangeSet` values do not update those fields until the graph commit
boundary accepts the change. This lets persistence providers store graph-level
ordering and resource-level revisions independently, while keeping
`ResourceDefinition`, `ResourceState`, and `ResourceRecord` document/store
shapes serializer-friendly.

For the server application shape, the POC also allows a single in-memory
`ResourceGraphModel` to own the current graph snapshot. The model loads from
`IResourceStateProvider`, hands out trackers based on the current snapshot,
commits changes back through the state provider, and only updates its cached
snapshot from the committed provider result. This lets the server keep the
resource graph hot in memory when there is one primary graph consumer, while
still preserving explicit commit boundaries, optimistic graph versions, and a
clear synchronization point with the backing data store.

Before a server-hosted `ResourceGraphModel` commits changes, it should refresh
the current snapshot from `IResourceStateProvider` and compare the change
set's base graph version with the stored graph version. If the store has moved
forward, the model updates its cache and returns a version-conflict result
instead of attempting to write stale changes. A stored graph version higher
than the change set's base version is therefore a direct indication that the
commit cannot be applied as-is; the caller needs to refresh the graph, or a
relevant part of it, and create a new change set against the newer version.
The state provider must still perform its own optimistic version check during
the actual commit because a second consumer can update the store after the
preflight read.

Commit results should summarize the outcome in addition to returning
diagnostics and the committed snapshot. `ResourceGraphCommitResult` carries a
`ResourceGraphCommitSummary` with a status such as committed, no changes,
rejected, or version conflict; the base and resulting graph versions; accepted
resource count; attribute and capability change counts; and per-resource
revision movement. This gives callers a stable way to decide whether to update
UI state, append events, publish notifications, retry a stale change, or show
validation errors without reinterpreting the full change set.

### Event History and Event Sourcing

The graph commit boundary is also a natural place to produce a durable change
history. A committed `ResourceGraphChangeSet` has the information needed to
append events such as resource attributes changed, capability payload changed,
operation declaration changed, resource state committed, or provider operation
executed. Those events can include graph version, resource ID, resource
revision, timestamp, actor/context, incremental `ResourceDefinition` deltas,
provider diagnostics, and provider-specific correlation data.
`ResourceGraphCommitSummary` is the immediate result-object surface for those
same facts, while a future event log would be the durable historical stream.

The POC should not make pure event sourcing the only source of truth yet.
Resource state is a resolved model over provider-owned reality, and providers
may reconcile against external systems, reject changes, or project runtime
facts that are not clean CloudShell-owned domain events. Full replay also
creates extra questions around provider behavior, schema evolution, graph-wide
cascading, and the boundary between desired state and observed runtime state.

The pragmatic direction is a hybrid model:

- `ResourceState` and `ResourceGraphSnapshot` remain the current persisted
  read model and fast server-hydration source.
- Successful graph commits may append a durable event/change record beside the
  snapshot.
- The event log can power audit history, a user-facing resource changelog,
  debugging, rebuilds, and future replay experiments.
- `ResourceGraphModel` can hydrate from the latest snapshot first and later
  replay committed events after that snapshot if a persistence provider
  supports it.
- Provider acceptance and external reconciliation remain explicit at the
  commit/provider boundary instead of being hidden inside event replay.

This keeps event-sourcing concepts available where they fit while avoiding an
early requirement that every provider-owned resource fact be reconstructable
from CloudShell events alone.

Typed projections can use the same boundary through a change context:

```csharp
using var changeContext = model.CreateChangeContext();
typedResource.ContainerReplicas = 2;

ResourceChangeSet changes = changeContext.ApplyChanges();
```

The exact source-generated shape is open, but property setters should not
silently commit provider-visible state or trigger graph-wide consequences.

The POC can keep these resource-type projection wrappers hand-written, but the
expected mature implementation is source-generated wrappers from the resource
class/type definitions, attribute IDs, capability IDs, and operation IDs. The
generated wrapper should be a convenience facade over the low-level
`Resource`, resolved values, and injected resolver services, not a second
source of truth for the resource model.

It is intentionally still open whether generated resource-type wrappers should
also implement capability-specific interfaces to advertise supported
capabilities, or whether capability support should remain discoverable only
through resolved capability declarations and resolver calls. Implementing
capability interfaces could make static use sites cleaner, but it also risks
making capability membership look like compile-time inheritance rather than
resolved provider-backed behavior.

This keeps interchange formats, persistence, low-level resource projection,
and runtime behavior separate. Serializers project definitions and resolved
debug views as data, while providers attach methods through the projection
layer at runtime. The same pattern can later be applied to operation
projections so operation implementations can consume capability projections
instead of duplicating capability-specific resolution.

Core and interchange structure:

```mermaid
flowchart TD
    resourceClassDefinition["ResourceClassDefinition<br/>shared class contract"]
    typeDef["ResourceTypeDefinition<br/>type contract within a class"]
    resourceClass["ResourceClass<br/>resolved class view"]
    resourceType["ResourceType<br/>resolved type view"]
    resourceDef["ResourceDefinition<br/>interchange model/format"]

    classValues["Attributes, capabilities, operations<br/>class defaults and requirements"]
    typeValues["Attributes, capabilities, operations<br/>type defaults and requirements"]
    resourceValues["Attributes, capabilities, operations<br/>rendered resource values and payloads"]

    resourceClassDefinition --> resourceClass --> resourceType
    typeDef --> resourceType
    resourceType --> resourceDef

    resourceClassDefinition --> classValues
    typeDef --> typeValues
    resourceDef --> resourceValues
```

Runtime resolution, providers, and generated wrappers:

```mermaid
flowchart TD
    subgraph definitions [Core and interchange inputs]
        deploymentDefinition["Deployment definition<br/>desired graph to apply"]
        resourceClassDefinition["ResourceClassDefinition"]
        typeDef["ResourceTypeDefinition"]
        resourceDef["ResourceDefinition"]
        resourceState["Resource-owned state<br/>persisted attributes, capabilities, operations"]
    end

    subgraph resolvedLayer [Resolved resource values]
        resolver["ResourceResolver"]
        resolved["Resolved attributes, capabilities, operations"]
        applyDefinition["Apply ResourceDefinition<br/>updates resource-owned state"]
    end

    subgraph behaviorLayer [Provider behavior]
        capabilityProviders["Capability providers<br/>capability-owned behavior"]
        operationProviders["Operation providers<br/>operation-owned behavior"]
        resourceChanges["Resource state changes"]
    end

    subgraph resourceViewLayer [Low-level resource projection]
        resource["Resource<br/>resolved projected state"]
        resourceClass["ResourceClass<br/>resolved class view"]
        resourceType["ResourceType<br/>resolved type view"]
        capabilityResolver["ResourceCapabilityResolver<br/>resolves capability providers"]
        operationResolver["ResourceOperationResolver<br/>resolves operation providers"]
    end

    subgraph wrapperLayer [Generated wrappers]
        projectionResolver["ResourceProjectionResolver<br/>selects wrapper by resource type"]
        wrapper["ExecutableApplicationResource<br/>source-generated facade"]
        method["GetVolumesAsync()<br/>wrapper method"]
    end

    deploymentDefinition --> resourceDef --> applyDefinition --> resourceState
    resourceClassDefinition --> resourceClass
    typeDef --> resourceType
    resourceClass --> resourceType
    resourceClass --> resolver
    resourceType --> resolver
    resourceState --> resolver
    resolver --> resolved --> resource
    resourceClass --> resource
    resourceType --> resource
    resourceState --> resource

    resource --> capabilityResolver
    resource --> operationResolver
    capabilityResolver --> capabilityProviders
    operationResolver --> operationProviders
    capabilityProviders --> resourceChanges
    operationProviders --> resourceChanges
    resourceChanges --> resourceState

    resource --> projectionResolver --> wrapper --> method
    method --> capabilityResolver
    operationProviders --> capabilityResolver
```

The same principle applies to projected resources. A `Resource` projection can
be checked against its known class/type expectations, but callers should use a
validation or resolution helper rather than assuming the projected attribute
dictionary is complete and valid.

## Resource Type Providers

A resource type provider should own the behavior for a precise resource type or
provider-backed family of resource types.

Responsibilities:

- declare supported resource type IDs
- declare the expected `ResourceClass`
- describe supported capabilities for the resource type
- describe supported operations for the resource type
- contribute or reference the `ResourceTypeDefinition`
- parse or adapt the provider-owned configuration payload
- apply defaults and normalize resource-owned state
- validate type-specific configuration
- plan and apply resource definition changes
- apply changes, update persisted state, and tear down resource state
- project persisted resource state and observed provider state as `Resource`
  instances
- expose resource operations and operation availability where applicable

Resource type providers may expose typed facades such as
`ExecutableApplicationResourceDefinition`, `ContainerApplicationDefinition`, or
`VolumeResourceDefinition`. Those facades should map to and from the common
definition envelope instead of replacing it as the platform model.

Resource type providers should live behind their own resource-type or
capability-package boundary. Avoid rebuilding the application-provider tangle
where unrelated resource types share a broad service because they all happen
to use local processes, containers, lifecycle actions, or Resource Manager
projection. Shared code is appropriate only when it represents a provider-
neutral platform mechanism or a deliberately shared capability contract. Code
that knows a concrete resource type, provider configuration shape, lifecycle
quirk, projection attribute, or runtime materialization rule should stay next
to the provider that owns that behavior.

Identifier constants should follow the same ownership rule:

- Platform-wide default attribute IDs can live in a shared constants class.
- Resource type IDs belong with the resource type provider or its package.
- Attribute IDs that are unique to one resource type belong beside that
  resource type provider.
- Capability IDs can initially live as constants on the concrete capability
  provider implementation that owns the capability.
- Operation IDs should be declared by the operation definition owner at the
  class, type, or resource-definition level, the same way attributes are
  declared by the layer that owns them. CloudShell-standard lifecycle
  operation IDs can live in a shared class. Type-specific operation IDs belong
  beside the resource type definition/provider that declares them.

This keeps a resource type provider's public surface reviewable: a reader
should be able to find the type ID, type-specific attributes, supported
capabilities, supported operations, validation rules, and projection behavior
inside the owning boundary instead of following references through a generic
application-resource service.

Resource type providers are integration points, so they may be constructed
with provider-owned or Control Plane services when validation, projection,
apply planning, or operation/capability resolution needs them. That does not
make them owners of background execution. For the POC, a type provider should
describe and validate graph state, project resource views, and delegate to
explicit integrations; runtime task loops, continuous health checks, and
reconciliation schedulers belong in Resource Manager, Control Plane services,
or provider-owned runtime services that call into the graph model at defined
boundaries.

Typed facades and builders can remain hand-written while the model is small or
still changing. If resource type definitions become structured enough that
facades, builders, descriptor constants, validation stubs, or JSON mapping code
become repetitive, CloudShell should consider C# source generators. Source
generation should be treated as an implementation aid over the definition
model, not as the source of truth. The durable contract remains the
`ResourceClassDefinition`, `ResourceTypeDefinition`, `ResourceDefinition`, and
resolved-resource model.

For example, an executable application resource type provider could own the
`application.executable` type while delegating storage mounts and start/stop
operations to DI-backed attached providers:

Sample provider package structure:

```mermaid
flowchart TD
    subgraph package [CloudShell.Providers.ExecutableApplications]
        registration["AddExecutableApplicationProvider()<br/>DI registration boundary"]
        typeProvider["ExecutableApplicationResourceTypeProvider<br/>IResourceTypeProvider"]
        typeDefinition["ResourceTypeDefinition<br/>application.executable"]
        ids["ExecutableApplicationIds<br/>type, attributes, operations"]
        payloads["Payload contracts<br/>configuration and operation payloads"]
        wrapper["ExecutableApplicationResource<br/>generated resource wrapper"]
        definitionFacade["ExecutableApplicationDefinition<br/>optional typed authoring facade"]

        subgraph capabilities [Provider-owned or referenced capabilities]
            volumeCapability["VolumeConsumerCapabilityProvider<br/>IResourceCapabilityProvider"]
            identityCapability["IdentityCapabilityProvider<br/>IResourceCapabilityProvider"]
        end

        subgraph operations [Provider-owned operations]
            startOperation["ExecutableStartOperationProvider<br/>IResourceOperationProvider"]
            stopOperation["ExecutableStopOperationProvider<br/>IResourceOperationProvider"]
        end

        subgraph internals [Provider internals]
            store["Definition/runtime store"]
            runner["Process runner"]
            state["Runtime state projector"]
        end
    end

    registration --> typeProvider
    registration --> volumeCapability
    registration --> identityCapability
    registration --> startOperation
    registration --> stopOperation

    typeProvider --> typeDefinition
    typeProvider --> ids
    typeProvider --> payloads
    typeProvider --> definitionFacade
    typeProvider --> wrapper

    wrapper --> volumeCapability
    wrapper --> identityCapability
    startOperation --> runner
    startOperation --> volumeCapability
    stopOperation --> runner
    state --> wrapper
    store --> typeProvider
```

```csharp
public sealed class ExecutableApplicationResourceTypeProvider(
    IExecutableApplicationDefinitionStore definitions,
    IEnumerable<IResourceCapabilityProvider> capabilityProviders,
    IEnumerable<IResourceOperationProvider> operationProviders)
    : IResourceTypeProvider
{
    public string TypeId => "application.executable";

    public ResourceClass ResourceClass => ResourceClass.Executable;

    public IReadOnlyList<ResourceCapabilityDescriptor> SupportedCapabilities =>
    [
        new("storage.volumeConsumer"),
        new("logs.sources"),
        new("monitoring")
    ];

    public ResourceDefinitionValidationResult Validate(
        Resource resource,
        ResourceProviderContext context)
    {
        var executable = resource.GetConfiguration<ExecutableConfiguration>(
            "executable");

        var diagnostics = new List<ResourceDefinitionDiagnostic>(
            resource.Diagnostics);

        if (string.IsNullOrWhiteSpace(executable.Path))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                resource.Name,
                "Executable path is required."));
        }

        foreach (var capability in resource.Capabilities)
        {
            var provider = capabilityProviders.FirstOrDefault(provider =>
                provider.CanValidate(resource, capability.Id));

            if (provider is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    resource.Name,
                    $"No provider is registered for capability '{capability.Id}'."));
                continue;
            }

            diagnostics.AddRange(provider.Validate(resource, context).Diagnostics);
        }

        return ResourceDefinitionValidationResult.FromDiagnostics(diagnostics);
    }

    public Resource Project(
        Resource resource,
        ResourceProjectionContext context)
    {
        var executable = resource.GetConfiguration<ExecutableConfiguration>(
            "executable");

        var operations = operationProviders
            .Where(provider => provider.CanHandle(resource))
            .Select(provider => provider.ProjectOperation(resource, context))
            .ToArray();

        return new Resource(
            Id: resource.EffectiveResourceId,
            Name: resource.Name,
            Kind: TypeId,
            Provider: "applications.executable",
            Region: "local",
            State: context.GetLifecycleState(resource.EffectiveResourceId),
            Endpoints: context.GetEndpoints(resource.EffectiveResourceId),
            Version: resource.State.Version,
            LastUpdated: context.Now,
            DependsOn: resource.State.DependsOn,
            TypeId: TypeId,
            Operations: operations,
            ResourceClass: ResourceClass,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ExecutablePath] = executable.Path,
                [ResourceAttributeNames.WorkingDirectory] =
                    executable.WorkingDirectory ?? "."
            },
            Capabilities: context.ProjectCapabilities(resource));
    }

    public Task<ResourceApplyResult> ApplyAsync(
        Resource resource,
        ResourceApplyContext context,
        CancellationToken cancellationToken)
    {
        var executable = resource.GetConfiguration<ExecutableApplicationResourceDefinition>("executable");
        definitions.Save(executable);

        return Task.FromResult(ResourceApplyResult.Accepted(resource.EffectiveResourceId));
    }
}
```

The resource type provider owns the type's configuration and projection shape.
It does not need to know every cross-cutting capability or every executable
operation implementation in detail. Capability and operation providers can be added
by capability packages through DI as long as they use stable resource type,
capability, and operation identifiers.

## Resource Change Application

Resource type providers should be able to respond before staged projection
changes are accepted as resource state. The provider receives a
`ResourceChangeSet`, which contains the current `Resource`, proposed
`ResourceState`, and attribute/capability diffs. It can return diagnostics,
accept the proposed state, or reject the change.

The current POC shape is intentionally small:

```csharp
public interface IResourceChangeApplyProvider
{
    ResourceTypeId TypeId { get; }

    bool CanApply(ResourceChangeSet changes);

    ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken);
}

public sealed record ResourceChangeApplyResult(
    ResourceChangeSet ChangeSet,
    ResourceState? AcceptedState,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics);
```

This is not the same level as applying a `ResourceDefinition` interchange
document to a graph. A `ResourceDefinition` can be rendered from a
`ResourceChangeSet`, or a `ResourceDefinition` can be applied to resource-owned
state before resolution, but provider-owned change acceptance acts on the
resolved `Resource` projection and its proposed resource state.

The future Resource Manager model can add a broader transaction or graph
commit pipeline on top:

```csharp
using var changeContext = model.CreateChangeContext();
resource.SetAttribute(NamedAttributeIds.ContainerReplicasAttributeId, 2);

ResourceChangeSet staged = changeContext.ApplyChanges();
ResourceChangeApplyResult accepted =
    await changeApplyDispatcher.ApplyChangesAsync(staged, context, cancellationToken);

await using var transaction = await graphModel.BeginTransactionAsync(cancellationToken);
transaction.Track(accepted);

ResourceGraphCommitResult commit =
    await transaction.CommitAsync(commitContext, cancellationToken);
```

The naming is still open. This boundary may end up being called a transaction,
a graph change context, or something else. The important requirement is that
CloudShell has a clear scope where graph changes are staged, validated,
accepted, and flushed to the backing store as one unit. For workflows that need
stronger coordination, that scope should be able to become exclusive so no
other writer can modify or flush graph changes to the same store while the
scope is active. The conservative POC shape is an in-process exclusive graph
boundary; a distributed Control Plane would still need store-backed locking,
leases, or transaction support above this model.

For a hypothetical container type provider, changing `container.image` while
the resource is stopped may only update accepted resource state. Changing it while the
resource is running may plan a deployment operation. Changing it while the
resource is already transitioning may be rejected, deferred, or folded into
the current transition depending on provider policy. The provider needs both
the changed attributes and the actual current status to make that decision.

Change planning should return diagnostics and an explicit plan rather than
forcing the caller to infer behavior from changed fields alone. The plan can
describe whether the change is persist-only, requires restart, can reconcile
in-place, starts a deployment operation, is blocked by current state, or needs
manual intervention. That richer planning belongs in a later Resource Manager
or provider orchestration layer; the POC only proves the low-level resource
projection can stage changes, route them to the owning type provider, and
commit accepted changes together as a versioned resource graph. In a server
process, `ResourceGraphModel` can keep that graph in memory and synchronize it
through the same provider commit boundary rather than forcing each operation
to rematerialize the whole graph from storage.

## Transactions and Staged Changes

The core model should distinguish committed state from staged changes. A
`ResourceChangeSet` is a proposed change against a base graph version; it is
not a new resource version and it does not represent committed resource state.
Callers may stage attribute or capability changes freely inside a change
context or future transaction because those changes are outside the committed
model until the graph commit boundary accepts them.

Versions are assigned only when changes become committed state:

- `ResourceGraphSnapshot.Version` identifies the committed graph snapshot.
- `Resource.Revision` identifies committed resource state.
- `ResourceChangeSet` carries proposed changes and the base graph version it
  was prepared against.
- `ResourceGraphTransaction` owns staged accepted changes for a graph snapshot
  and commits them through `ResourceGraphModel`.
- Future transaction slices can add provider validation orchestration,
  conflict policy, diagnostics enrichment, summary enrichment, and event
  creation.

That transaction layer sits above the core resource projection. The core model
resolves state and allows proposed changes to be expressed; the transaction or
resource graph management layer decides whether those proposals can become
committed state. In the conservative default, a transaction commits only when
the stored graph version still matches its base graph version. If the stored
graph is newer, the transaction is rejected with a version conflict and the
caller refreshes the graph or relevant resources before creating a new change
set. Future merge or rebase support should be explicit, provider-aware, and
auditable rather than implicit in `Resource` or capability projections.

The current POC transaction is intentionally small. It captures the base
`ResourceGraphSnapshot`, tracks accepted `ResourceChangeApplyResult` values,
exposes the pending `ResourceGraphChangeSet` for inspection, and can be
committed once through the graph model. It prevents reusing a completed
transaction, but it does not yet resolve resources, run capability providers,
or apply merge policies itself.

The POC also supports an opt-in exclusive mode for the in-memory
`ResourceGraphModel`. Exclusive mode holds the model lock for the graph change
boundary and blocks other in-process graph reads, commits, refreshes, and
reloads until the boundary commits or is disposed. This proves the shape of an
atomic change boundary without making the core Resource model responsible for
the final distributed locking or transaction policy.

## Capability Providers

Capability providers are attached behavior for resolved capabilities. They
should be registered with dependency injection and resolved by the Control
Plane validation/apply pipeline, so a provider can depend on platform or
provider services such as volume managers, identity managers, networking
managers, policy services, catalogs, or stores.

Capabilities are also integration points. The owner of a capability
implementation does not have to be the resource type provider. Resource
Manager, an orchestrator, a provider package, or another Control Plane service
can own the concrete capability provider when that owner needs to inject its
own services, enforce its own policy, or coordinate its own domain logic. The
Resource model declares and resolves the capability; the implementation can
belong to the boundary that owns the behavior.

Responsibilities:

- declare the capability ID they handle
- parse or adapt the capability payload for that capability
- validate capability-owned state against the resolved resource and current
  environment
- report diagnostics for invalid, unsupported, unsafe, or unresolved state
- provide typed helper behavior to resource type providers, orchestrators, or
  projection services where appropriate
- project typed runtime behavior as a resource-bound capability work unit
  through a capability resolver
- optionally contribute resolved capabilities, dependencies, attributes, or
  diagnostics after the resource state has been accepted

Capability providers should validate resolved `Resource` projections, not raw
`ResourceDefinition` interchange inputs. Raw interchange inputs are missing
inherited class/type/preset values. The resolved `Resource` gives the provider
the effective capability entry, resource attributes, operation declarations,
type/class views, current environment, and provider observations needed for
validation.

Projected capabilities should be resource-bound behavior, not graph transaction
owners. The caller that resolves and executes a capability or operation should
own the resource graph scope it is operating within, including any snapshot,
transaction, lock, apply dispatcher, and commit boundary. This keeps graph
stability and concurrency policy at the orchestration boundary instead of
passing graph snapshots or lock handles into capability or operation execution
contexts.

Capability methods can stage changes through the target `Resource` and return
`ResourceChangeSet` values. The caller can then validate or apply those changes
through the relevant resource type provider and decide whether to track and
commit them through a graph transaction. This keeps capability methods
ergonomic while preventing hidden graph writes from a capability projection
that was created for inspection.

As an immediate rule, projected capabilities should stage direct Resource model
changes only for the resource they are attached to. A capability can call out
to Resource Manager or other services for integration behavior, but graph-wide
Resource model side-effects and cross-resource graph mutations need an
explicit future scope or isolation model before they become part of the
Resource model contract.

If a capability needs to reject raw authored document shape before resource
resolution, that should be modeled as a resource-definition validator for the
interchange layer. It may use the same capability ID, but it is not the
capability provider that attaches behavior to the resolved resource.

For example, a storage volume consumer provider can own the
`storage.volumeConsumer` capability:

```csharp
public sealed class VolumeConsumerCapabilityProvider(IVolumeManager volumes)
    : IResourceCapabilityProvider
{
    public string CapabilityId => "storage.volumeConsumer";

    public ResourceDefinitionValidationResult Validate(
        Resource resource,
        ResourceProviderContext context)
    {
        var declaration = resource.Capabilities.Resolve(CapabilityId);

        // Validate mount shape, referenced volume resources, access mode,
        // permissions, and host/storage compatibility.
    }

    public IEnumerable<Volume> GetVolumes(
        Resource resource)
    {
        var volumeConsumer =
            resource.Capabilities.Get<VolumeConsumerCapability>();

        return volumeConsumer.Mounts
            .Select(mount => volumes.GetVolume(mount.VolumeReference));
    }
}
```

This keeps storage behavior reusable across executable apps, ASP.NET Core
projects, container apps, SQL Server resources, or future provider-owned
service resources without pushing volume semantics into each resource type.
That reuse should happen through the capability provider contract and its
owned constants/payload shape, not by making one resource-type provider depend
on another resource-type provider's implementation internals.

## Attribute Validators

Attribute validation should be explicit and reusable. Attributes are useful
only when callers can understand whether an attribute is required, inherited,
defaulted, supplied by the instance definition, projected by the provider, or
invalid for the resource's class/type.

Attribute validators should cover common rules:

- required value
- string, number, boolean, enum-like token, URI, path, resource reference, and
  structured payload validation
- allowed values
- range and length checks
- pattern checks
- case normalization
- invariant formatting
- read-only attribute enforcement for authored or applied changes
- secret-value rejection
- provider compatibility
- cross-attribute rules

They should also allow type-specific and capability-specific rules without
forcing every rule into a central switch. For example:

```csharp
public sealed class ExecutablePathAttributeValidator : IResourceAttributeValidator
{
    public string AttributeName => ResourceAttributeNames.ExecutablePath;

    public bool CanValidate(ResourceAttributeValidationContext context) =>
        context.Type.TypeId == "application.executable";

    public ResourceAttributeValidationResult Validate(
        ResourceAttributeValue value,
        ResourceAttributeValidationContext context)
    {
        if (value.IsMissing)
        {
            return ResourceAttributeValidationResult.Error(
                AttributeName,
                "Executable path is required.");
        }

        if (!value.IsString)
        {
            return ResourceAttributeValidationResult.Error(
                AttributeName,
                "Executable path must be a string.");
        }

        return ResourceAttributeValidationResult.Valid(AttributeName);
    }
}
```

Validation should happen at two related boundaries:

- definition validation: does the authored `ResourceDefinition` satisfy its
  class/type/capability/operation requirements?
- projection validation: does the projected `Resource` still satisfy the known
  `ResourceClassDefinition` and `ResourceTypeDefinition` expectations?

Projection validation matters because provider projections can drift, omit
inherited values, or carry legacy attribute names. Resource Manager and API
clients should be able to surface diagnostics or normalized views instead of
silently trusting raw projected attributes.

## Resource Operation Providers

Resource operation providers are attached behavior for a declared resource
operation. They should be registered with dependency injection and resolved by
the Control Plane when it projects resource commands, computes command
availability, executes a requested command, reconciles state, or applies other
provider-owned behavior. Operation providers should resolve the operation
declaration they handle from the resolved resource context at the level they
explicitly support: class, type, resource state, or a combination of those
levels.

Operations are also integration points. Resource Manager can own an operation
provider when the operation is part of the Resource Manager or Control Plane
domain and needs Resource Manager services, authorization context,
orchestration state, procedure dispatch, activity logging, or provider runtime
coordination. The Resource model should define the operation declaration and
make it resolvable on the resource graph; it should not require the operation
implementation to live beside the resource type definition.

Operation projections follow the same pattern as capability projections: they
are resource-bound work units resolved from a `Resource`, an operation ID, and
an optional resolution level. The projection can expose methods and
properties for that operation while the provider owns the implementation. The
wrapper that consumes the operation should not pass a `ResourceDefinition`
back into it; changes can be rendered to interchange only when the operation
needs to return a proposed resource-state update.

Operations that can be executed can opt into a generic executable operation
projection contract. That gives Resource Manager, an orchestrator, or another
Control Plane service a provider-neutral way to check execution availability
and invoke the operation after resolving it from the graph, while still
allowing provider-specific typed wrappers to expose richer methods when they
need them.

Like capabilities, operation projections should stay resource-bound. The
caller that resolves and invokes the operation owns the graph snapshot,
transaction, lock, apply dispatcher, and commit boundary. If an operation
needs to stage model changes, it can return or expose resource change sets
from the target `Resource`; the caller decides how those changes are applied,
tracked, and committed.

As with capabilities, direct Resource model changes produced by an operation
should be limited to the attached resource for the POC. Operations can still
trigger Resource Manager behavior or other integration logic, but any
cross-resource graph mutation or graph-wide Resource model side-effect needs a
later scoped execution or isolation concept.

The operation declaration is the resource model contract. The provider is the
implementation. Two resource types can declare the same operation ID while
using different operation providers because the concrete implementation may be
type-specific, provider-specific, host-specific, or capability-specific. For
example, a `start` operation can be declared broadly for executable resources,
while local processes, container apps, and provider-backed services use
different providers to execute the resulting start command.

Operation IDs should not be tangled into capability builders or capability
payload helpers. A capability can require, enable, or constrain an operation,
but the operation ID itself belongs to the class/type/resource-definition
operation declaration. This leaves room for the same operation ID to have
different implementations for different target artifacts without making a
capability provider the accidental owner of that operation.

Responsibilities:

- advertise the operation ID they handle
- declare the resource types, resource classes, or capabilities they can
  handle
- declare which operation resolution levels they handle
- project the resource operation and any caller-facing command affordance when
  the operation applies to a resource
- compute current command availability and user-displayable unavailable
  reasons
- execute or apply the backing operation after Control Plane authorization and
  validation
- return resource procedure results, diagnostics, activity events, or
  reconciliation signals

Resource commands are not UI commands. A Resource Manager button, menu item, or
API route can invoke a resource command, but the operation provider owns the
domain behavior behind that command.

For example, an executable start operation provider can handle the resolved
standard `start` operation for executable application resources:

```csharp
public sealed class ExecutableStartOperationProvider(
    IExecutableApplicationDefinitionStore definitions,
    ILocalProcessRunner processes,
    IResourceCapabilityProvider<VolumeConsumerDefinition> volumes)
    : IResourceOperationProvider
{
    public string OperationId => ResourceOperationIds.Start;

    public ResourceOperationResolutionLevel ResolutionLevel =>
        ResourceOperationResolutionLevel.Type;

    public bool CanHandle(Resource resource) =>
        resource.Type.TypeId == "application.executable" &&
        resource.Operations.Resolve(OperationId, ResolutionLevel).IsAvailable;

    public ResourceOperation ProjectOperation(
        Resource resource,
        ResourceProjectionContext context) =>
        new(
            Id: ResourceOperationIds.Start,
            Label: "Start",
            Description: "Start the executable application.",
            RequiresConfirmation: false);

    public async Task<ResourceCommandAvailability> GetAvailabilityAsync(
        Resource resource,
        ResourceCommandAvailabilityContext context,
        CancellationToken cancellationToken)
    {
        var operation = resource.Operations.Resolve(OperationId, ResolutionLevel);
        if (!operation.IsAvailable)
        {
            return ResourceCommandAvailability.Unavailable(
                ResourceCommandIds.Start,
                operation.UnavailableReason ?? "The start operation is not available.");
        }

        if (context.State is ResourceState.Running or ResourceState.Starting)
        {
            return ResourceCommandAvailability.Unavailable(
                ResourceCommandIds.Start,
                "The resource is already running or starting.");
        }

        var volumeDiagnostics = await volumes.ValidateAsync(
            resource,
            context.ToDefinitionValidationContext(),
            cancellationToken);

        if (volumeDiagnostics.HasErrors)
        {
            return ResourceCommandAvailability.Unavailable(
                ResourceCommandIds.Start,
                "One or more volume mounts cannot be materialized.");
        }

        return ResourceCommandAvailability.Available(ResourceCommandIds.Start);
    }

    public async Task<ResourceProcedureResult> ExecuteAsync(
        Resource resource,
        ResourceCommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var operation = resource.Operations.Resolve(OperationId, ResolutionLevel);
        if (!operation.IsAvailable)
        {
            return ResourceProcedureResult.Failed(
                operation.UnavailableReason ?? "The start operation is not available.");
        }

        var executable = definitions.Get(resource.EffectiveResourceId);
        if (executable is null)
        {
            return ResourceProcedureResult.Failed(
                $"Resource '{resource.EffectiveResourceId}' was not found.");
        }

        await processes.StartAsync(executable, cancellationToken);

        return ResourceProcedureResult.Completed(
            $"Started executable application '{resource.Name}'.");
    }
}
```

This lets a resource type support multiple commands without centralizing every
backing operation in the resource type provider. Standard lifecycle commands
can have shared policy in the Control Plane, while provider-specific operation
providers still own provider-specific checks and execution.

## Capability Lifecycle

CloudShell should distinguish these phases:

| Phase | Meaning |
| --- | --- |
| Resource type capability support | The type can accept resources that use the capability. |
| Resource capability declaration | The resource-owned state declares capability-owned data. |
| Resolved capability | Resolution and validation accepted the capability into `Resource.Capabilities`. |
| Projected resource capability | The current `Resource` advertises the resolved capability for discovery. |
| Runtime materialization | A provider, orchestrator, or runtime has applied or observed the capability in the environment. |

For example, `application.container-app` may support
`storage.volumeConsumer`; a specific container app resource declares two
mounts; validation accepts the mounts; the resolved resource advertises
`storage.volumeConsumer`; and runtime materialization later reports whether the
mounts are active.

## Persistence, Debugging, and Plain Format

Resource-model artifacts should be plain enough to inspect and review, but
CloudShell should distinguish the domain model, serialized interchange
formats, and internal persistence records.
`ResourceDefinition`, `ResourceTypeDefinition`, `ResourceClassDefinition`, and
related definition artifacts should be serializable as deliberate
interchange formats for JSON, YAML, XML, templates, imports,
exports, diagnostics, tests, and review. That serialized projection is a
portable representation of the model, not a requirement that
CloudShell persist the exact same shape internally.

Persistence is a core concern for the Resource model, but there are separate
questions that should not be collapsed too early:

1. What document or interchange format represents the model, and how is that
   format saved and loaded?
2. How does CloudShell persist resource-owned state: individual resources,
   whole graph snapshots, collections of accepted changes, or incremental
   change documents?

Whether a specific store is in-memory, database-backed, file-backed, or a
combination of those is a lower-level provider and hosting choice. The POC
should prove the model can save, load, and commit resource-owned state without
optimizing prematurely around one storage backend. Before choosing that store
shape, the POC should first demonstrate how Resource Manager consumes and
manages a resource graph defined by the Resource model. That integration will
tell us which data belongs in the Resource model, which data belongs in the
Control Plane operational model, and what persistence boundary the store
actually needs to support.

CloudShell persistence should store the resource-owned state on `Resource`:
identity, type, dependencies, provider-owned payloads, and the attributes,
capabilities, and operations defined on that resource instance. The
persistence model can use a store-optimized representation, such as a
`ResourceRecord`, normalized tables, provider-owned persistence records, or a
compact JSON file. That persistence shape may split out identity fields,
dependencies, attributes, capability declarations, operation declarations,
provider payloads, ownership, grouping, indexes, and migration metadata.

The persistence record should remain an implementation detail and must
rehydrate into the same core resource model before validation, resolution,
planning, projection, provider behavior, or deployment apply runs.
`ResourceDefinition` remains the interchange projection of that resource
state, not the required internal persistence shape.

`Resource` should also be serializable as a debug or
diagnostic snapshot so callers can inspect which attributes, capabilities,
operations, defaults, sources, and diagnostics were effective after
resolution. It is a computed view of resolved values, not the primary state
container.

The low-level `Resource` projection sits above those data shapes as the
resolved state object in this model. It combines persisted resource-owned
state, resolved type/class values, actual accepted or observed state, and
resolved capability and operation behavior. Generated wrappers sit above that
projection as the upper domain-model API. A generated resource-type wrapper,
such as an
`ExecutableApplicationResource`, can expose typed properties and methods over
the low-level `Resource` while internally resolving capability and operation
providers. Those wrappers are how domain code should consume behavior-rich
resource views; they are not the persistence record and they are not the
portable serialized interchange format.

This does not replace the Control Plane's own resource manager model. The
Control Plane may store additional resource records for ownership,
authorization, grouping, procedures, liveness signals, operational state,
logs, traces, and provider runtime metadata. Those records are complementary
server-side state around the resource graph. They may share identity with this
POC's `Resource` projection and may materialize or consume it, but they are
not required to use the same persistence shape or to expose every Control
Plane operational concern through the low-level resource definition model.

One possible midway integration is a store-backed graph projector that can
load graph resources from any Resource Manager-owned store shape. For example,
the Resource Manager database could keep its own resource row for operational
state while storing the Resource model graph payload as JSON in a column on
that same resource row. A projector would hydrate that JSON graph record into
`ResourceState` for graph resolution and then persist accepted graph changes
back into the same uniform Resource Manager store. This keeps the POC
flexible: the graph does not need a separate database too early, and the
database schema does not need to expose every graph field as columns before
the graph shape stabilizes.
The first code proof is intentionally in-memory: a custom Resource Manager row
can keep an operational-state field while the projector updates only its JSON
graph payload from accepted `ResourceState`. The Resource Manager bridge
exposes a generic in-memory graph registration overload for projected store
records, so hosts can supply their own Resource Manager-owned row type plus an
`IResourceGraphStoreProjector<TRecord>`.

Layered definition, persistence, and projection model:

```mermaid
flowchart TD
    subgraph domainData [Core resource model]
        resource["Resource<br/>resolved projected state"]
        resourceType["ResourceType<br/>resolved type view"]
        resourceClass["ResourceClass<br/>resolved class view"]
        classDefinition["ResourceClassDefinition"]
        typeDefinition["ResourceTypeDefinition"]
    end

    subgraph documentProjection [Interchange model and formats]
        definition["ResourceDefinition<br/>interchange model/format"]
        json["JSON"]
        yaml["YAML"]
        xml["XML"]
        templates["Templates and imports"]
    end

    subgraph persistenceProjection [CloudShell persistence projection]
        record["ResourceState or ResourceRecord<br/>stripped store-optimized data"]
        tables["Normalized tables"]
        compactJson["Compact resource JSON"]
        indexes["Indexes and metadata"]
    end

    subgraph resolvedData [Computed resolved values]
        resolver["ResourceResolver"]
        resolved["Resource<br/>complete value set"]
    end

    subgraph resourceInputs [Resource state inputs]
        actualState["Accepted or observed state"]
        capabilityOperationBehavior["Resolved capabilities and operations"]
    end

    subgraph upperApi [Upper domain-model API]
        wrapper["Generated resource wrapper"]
        behavior["Capability and operation methods"]
    end

    subgraph resourceManagerModel [Resource Manager model]
        managedResource["Resource Manager resource<br/>operational model"]
        liveness["Liveness, lifecycle, endpoints,<br/>authorization, logs, traces"]
    end

    resource -->|render| definition
    definition -->|apply changes| resource

    classDefinition --> resourceClass --> resourceType
    typeDefinition --> resourceType
    resourceType --> resource
    resourceClass --> resolver
    resourceType --> resolver
    resource --> resolver

    definition --> json
    definition --> yaml
    definition --> xml
    definition --> templates

    resource --> record
    record --> tables
    record --> compactJson
    record --> indexes
    record --> resource

    resolver --> resolved --> resource
    actualState --> resource
    capabilityOperationBehavior --> resource
    resource --> wrapper --> behavior
    resource --> managedResource --> liveness
```

The durable formats should avoid making C# builder types, generated DTO names,
or provider-native files the source of truth.

Suggested principles:

- Use stable, lower-camel or dotted identifiers for resource type,
  capability, and configuration keys.
- Use read-only attribute definitions for provider-projected state that should
  appear on the resolved Resource projection but should not be authored by
  deployment ResourceDefinition documents or rendered back into ResourceDefinition
  interchange output, such as a Docker container endpoint count before concrete
  endpoint objects exist in the graph model.
- Prefer value objects and typed IDs in .NET APIs for resource type IDs,
  attribute IDs, capability IDs, operation IDs, references, and source IDs.
  Those value objects should serialize as their stable string values through
  explicit converters or serializer-supported mappings so JSON, YAML, XML, and
  other projections remain plain, reviewable, and portable. A serializer should
  be able to round-trip the artifact without leaking implementation-only type
  details into the document.
- Include a definition version so providers can migrate payloads.
- Keep provider-owned configuration under `configuration`.
- Keep cross-cutting capability intent under `capabilities`.
- Keep secrets out of definitions. Store references to secret resources,
  configuration entries, or identity-backed access grants instead.
- Normalize before persistence only when normalization is deterministic and
  reviewable.
- Preserve enough source metadata for diagnostics when definitions are created
  from imports or templates.

Resource templates can become one serialized projection over this model rather
than a separate concept with unrelated provider configuration. Resource graph
imports can translate external dialects into resource definitions or graph
drafts before apply.

## Validation Pipeline

The Control Plane should eventually validate definitions through a predictable
pipeline:

1. Parse the definition envelope.
2. Resolve the resource class definition and resource type definition.
3. Apply selected presets and deterministic defaults.
4. Resolve inherited attributes, capabilities, and operations into an effective
   model.
5. Resolve the resource type provider.
6. Validate platform-owned identity, names, grouping, persistence, ownership,
   and references.
7. Reject caller-authored values or changes for attributes whose effective
   `ResourceAttributeDefinition` is read-only, except for trusted
   provider-owned projection paths.
8. Run common and type-specific attribute validators.
9. Let the resource type provider normalize and validate type-specific
   configuration.
10. Resolve capability providers for declared capability intent.
11. Let capability providers validate capability-owned payload shape and
    resource-local semantics.
12. Resolve resource operation providers for declared and type-supported
   operations.
13. Let operation providers validate operation configuration and command
   projection policy, including availability policy that can be checked before
   projection or apply.
14. Compute the definition diff when a `ResourceDefinition` update is being
   applied to an existing resource.
15. Let the resource type provider plan the definition change using the
   current resource, proposed resource state, changed
   attributes/capabilities/operations, and current runtime state.
16. Run cross-definition graph validation, including dependencies,
   capability references, authorization, compatibility, and host/provider
   policy.
17. Return diagnostics and normalized accepted resource state without side
    effects.
18. Apply, update, persist, or project only after validation succeeds.

Expected validation failures should be returned as diagnostics or result
objects. Exceptions should remain for programmer errors or boundary adapters
that must translate invalid input into API errors.

## Relationship to Existing Concepts

### Resource declarations

Programmatic declarations should become one authoring surface for
`ResourceDefinition`. Existing builders can continue producing provider-typed
definitions internally while the common envelope is introduced.

### Resource templates

Resource templates should eventually store resource definitions instead of
provider-specific configuration records that must be interpreted separately.
Template import/export providers may remain during migration, but their target
shape should converge on the common definition model.

### Resource graph import

External imports, such as Docker Compose, should translate into CloudShell
resource definitions or graph drafts. External formats remain input dialects,
not native CloudShell definition formats.

### Deployment projection

Deployment definitions should be able to contain `ResourceDefinition` entries
as interchange inputs for resource state. In that flow, a deployment
definition tells CloudShell which resource state changes an actor wants
applied, while each resource type provider validates, plans, and applies the
definition to the resource type it owns.

This makes resource definitions useful before a resource has been persisted as
accepted inventory. The same interchange envelope can describe a new resource
to create, changes for an existing resource, or a candidate graph that must be
validated before apply. The resource type provider remains the boundary that
maps accepted resource state to an executable, container, orchestrator
service, database, load balancer, or other managed target.

Deployment projection should consume proposed `ResourceDefinition`
interchange inputs, resolved `Resource` projections, and current graph
context. It should not infer resource state solely from projected
`Resource.Attributes` when a full `Resource` projection or apply input is
available.

### Projected resources

Provider-created and runtime-managed resources may be projected as `Resource`
instances without having user-authored definitions. If they later become
authorable, their provider should introduce a definition shape deliberately.

Projected resources should still be validated against known
`ResourceClassDefinition` and `ResourceTypeDefinition` expectations when those
definitions exist. Projection validation can produce diagnostics, normalize
legacy provider output, or explain why generated details and command
availability are incomplete.

## Recommended First Slices

1. Document the terminology across the domain and resource model docs:
   `Resource` is instance projection; `ResourceDefinition` is intent.
2. Introduce a public preview `ResourceDefinition` envelope in
   `CloudShell.Abstractions` without migrating every provider immediately.
3. Add preview `ResourceClassDefinition` and `ResourceTypeDefinition` records
   with inherited attribute, capability, and operation descriptors.
4. Add a resource-definition resolver that computes effective attributes,
   capabilities, operations, and diagnostics.
5. Add a resource-definition validation result and diagnostic model.
6. Add common attribute validators and one type-specific validator.
7. Add a resource type provider validation/normalization path for one narrow
   type, preferably `cloudshell.volume` or `application.executable`.
8. Add a capability-provider path for `storage.volumeConsumer` that validates
   `ResourceVolumeMount` intent outside application-specific code.
9. Add a resource-operation-provider path for one standard lifecycle operation,
   preferably executable `start` or container app `restart`.
10. Map one existing programmatic builder into the definition envelope.
11. Update resource template export/import for the same narrow type to use the
   definition format.
12. Add Control Plane tests for valid definitions, invalid attributes, invalid
   capability payloads, missing capability providers, missing operation
   providers, projection validation, and diagnostics.
13. Add API/client projection only after the in-process definition model is
   stable enough to expose.

## Open Questions

- Should `ResourceDefinition` use resource `name` plus `type`, resource `id`,
  or both as the primary identity in the serialized format?
- Should capability payloads live only under `capabilities`, or can a resource
  type provider promote common capability payloads into typed configuration
  facades for ergonomics?
- How much normalized resource state should be persisted versus recomputed
  from type/class definitions and current provider defaults?
- What is the precedence order between class defaults, type defaults, selected
  presets, provider defaults, and explicit resource-owned values?
- Should class/type definitions be public authoring artifacts, provider-only
  descriptors, or both?
- Should typed resource facades, builders, descriptor constants, or mapping
  helpers be generated from resource type definitions with C# source
  generators when the repetition becomes material?
- Should definition migrations be owned entirely by resource type providers, or
  should the Control Plane own a common migration registry?
- Which attribute validators belong in common abstractions versus provider
  packages?
- Should projection validation normalize invalid provider output, return
  diagnostics only, or support both modes?
- How should capability providers declare compatibility with resource types:
  type-provider metadata, capability-provider metadata, or both?
- How should class-level capability implementations be selected and overridden
  by resource types while preserving one stable capability contract?
- Which validation belongs in capability providers versus graph-level Control
  Plane policy?
- How should operation providers declare compatibility with resource types:
  operation-provider metadata, type-provider metadata, capability requirements,
  or all of those?
- What is the exact boundary between capability-driven functionality and
  operation-driven behavior when a concept has both, such as storage mounts or
  deployment?
- Should resource state be able to override inherited operations, and which
  class/type/resource-level operation declarations may be disabled or
  refined?
- Should command affordances always be projected from resolved operations, or
  should any command affordances be declared independently?
- What runtime state should be available to resource type providers when they
  plan a definition update, and how should transitioning resources be handled?
- How should persisted definitions represent provider selection when several
  providers can handle the same resource type?
- What is the minimal API surface for remote clients to create, validate, and
  persist definitions without exposing unstable provider internals?

## Remaining Tasks

- Define the `ResourceDefinition` envelope and serialized field names.
- Define `ResourceClassDefinition` and `ResourceTypeDefinition`, including
  inheritance, presets, requirements, and descriptor precedence.
- Define resolver services or helper methods for effective attributes,
  capabilities, and operations.
- Decide whether any typed resource facades or builders should be hand-written
  first or generated from resource type definitions later with C# source
  generators.
- Define resource type provider contracts for definition parsing,
  normalization, validation, projection, apply, update, and tear down.
- Define resource definition diff and change-planning contracts for resource
  type providers.
- Define common and provider-owned attribute validator contracts.
- Define capability provider contracts for capability-owned payload parsing,
  validation, diagnostics, and helper behavior.
- Define resource operation provider contracts for command projection, command
  availability, backing operation execution, diagnostics, and operation
  results.
- Decide how existing provider-specific definitions map to the envelope.
- Decide how resource templates, persisted declarations, imports, and create
  requests converge on the definition model.
- Add documentation examples for executable apps, container apps, volumes, and
  volume consumers.
- Add focused tests around definition validation and capability-provider
  resolution.
- Update the roadmap when this becomes an active implementation track.
