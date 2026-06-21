# Domain model

This document explains the core CloudShell concepts, where each concept is
modeled in code, and how the concepts are projected through the Control Plane
API.

For the focused definition of what a CloudShell resource is, how resources
relate to services, and how the resource object model maps endpoint and
relationship concepts, see [Resource model](resource-model.md).

CloudShell deliberately uses different levels of abstraction, but those levels
should use the same established concepts. Internal Control Plane services are
more granular because they coordinate providers, stores, and authorization.
Shell and integration consumers use higher-level managers. The HTTP API
projects the same domain entities through versioned contracts with hypermedia
affordances.

For implementation and verification checklists for each resource-model
artifact, see [Artifact implementation guidelines](artifact-implementation-guidelines.md).

## Abstraction levels

| Level | Audience | Purpose | Examples |
| --- | --- | --- | --- |
| Host topology | Product integrators and operators | Describe how CloudShell environment capabilities are packaged and deployed | CloudShell environment, host application, CloudShell UI, Control Plane, capability packages |
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

## Host topology

CloudShell separates the application that hosts the environment from the
capabilities installed into that environment.

A CloudShell environment is the managed local, team-owned, or on-premise
cloud-like environment that users inspect and operate. It is anchored by the
Control Plane's resource state and installed capability packages. It may be
served by a combined host application or by separate Control Plane and UI host
applications.

A CloudShell host application is the ASP.NET Core application owned by a
product integrator or sample. It chooses the deployment shape, configuration,
authentication, persistence, and installed capabilities. The host can run the
CloudShell UI, the Control Plane, or both together. In split deployments, the UI
host discovers resources through a remote Control Plane client instead of
declaring resources or hosting providers locally. In combined local-development
deployments, programmatically declared resources may run from the same host
process that hosts the UI and Control Plane, but they are managed by the same
local Control Plane that coordinates provider behavior, lifecycle actions, and
resource projection.

The CloudShell UI and Control Plane are separate application surfaces. The UI
renders shell experiences and uses public domain managers. The Control Plane
owns resource inventory, provider coordination, lifecycle operations,
validation, authorization, logs, templates, and the versioned API.

A CloudShell capability package is an installable capability for the host
application and environment. It may be vertical, such as Docker support,
application resources, configuration services, or secrets, or cross-cutting,
such as networking, identity, observability, deployment, or policy. A
capability package is intended to be distributed as one or more NuGet packages
referenced by host applications. It can contribute:

- Control Plane resource providers and provider-owned services.
- Resource type definitions and programmatic declaration helpers.
- Resource actions, logs, templates, diagnostics, and capabilities.
- Resource Manager UI support such as add/update components, tabs, detail
  routes, and UI actions.
- Shell-level UI such as navigation, workspaces, settings pages,
  notifications, named content areas, and operational dashboards.
- SDK clients or helper packages for authored services.

Capability packages are packaging and environment boundaries, not resource
model entities. A capability package can define several resource types, and a
resource can depend on capabilities from several packages. The runtime
configuration used to start an application resource remains a workload
descriptor, for example `ResourceWorkloadConfiguration`; that is distinct from
a CloudShell capability package. The code-level extension entry points inside a
capability package, such as `ICloudShellExtension` implementations and
`AddXyz(...)` registration methods, are how the NuGet package plugs into a host
application.

## Core concepts

### Resource and service terminology

CloudShell is a resource-oriented model over infrastructure that often provides
network-addressable services. Use these terms deliberately:

- **Resource**: the CloudShell management artifact. A resource is what the
  Control Plane registers, authorizes, groups, displays, operates, and exposes
  through the Resource Manager and Control Plane API.
- **Service**: the runtime or infrastructure capability contained in or
  provided by a resource, such as a Web API, SQL Server process, DNS publisher,
  load-balancer runtime, identity provisioner, configuration API, or Secrets
  Vault API.
- **Service resource**: the specific `cloudshell.service` resource kind. It is
  a resource that can model a service facade, service unit, imported service,
  or advanced routing target. It is not a synonym for every resource that
  provides a service.

When in doubt, use **resource** for the thing CloudShell manages and **service**
for the capability, process, API, or runtime behavior behind that resource.
For example, a container app resource may provide an application service, and a
SQL Server resource may provide a database service, but both are still
resources in the CloudShell model.

### Resource

A resource is the central CloudShell artifact. It represents something the
platform can inspect or operate, such as a Docker Engine, container, executable
application, configuration service, database, queue, or internal service.

In code, a resource is projected as `Resource`.

Important properties:

- `Id`: immutable platform identity or derived resource path.
- `Name`: scoped unique resource name.
- `DisplayName`: optional presentation label.
- `TypeId` / `EffectiveTypeId`: stable resource type.
- `ResourceClass`: broad resource classification such as executable, project,
  container, service, network, storage, configuration, or infrastructure.
- `Attributes`: stable, non-secret details that describe the resource's class,
  type, or provider-owned shape.
- `State`: optional lifecycle or health-oriented state produced by resources
  that expose lifecycle status.
- `Endpoints`: resource-owned named ports/protocols exposed by the resource.
- `ResourceEndpointNetworkMappings`: topology-specific addresses that map
  resource endpoints into a network.
- `DependsOn`: resource dependencies.
- `ParentResourceId`: parent/child hierarchy.
- `DetailRoute`: optional extension-owned UI detail route.
- `ResourceActions`: resource-domain operations exposed by the provider.
- `ResourceHealthChecks`: health signals contributed by providers.
- `ResourceCapabilities`: standardized or provider-owned capabilities the
  resource can provide to the environment, such as endpoint sources or
  networking providers.

`Resource` is a uniform projection. It is not subclassed for container apps,
runtime containers, executables, projects, services, or infrastructure. A
resource carries common
attributes such as class, type, endpoints, actions, health checks,
observability, and structural metadata; providers own the configuration and
runtime behavior behind those attributes. `Resource` does not imply CloudShell
owns all underlying provider configuration or runtime state.

`Id` is the canonical resource identity and should be visible wherever users
inspect resource details. Activity logs should use the resource ID as the
canonical resource address so lifecycle actions, provider-scoped events, and
procedure milestones remain traceable even when display names are enabled.
`Name` is the scoped unique name that users and programmatic declarations
normally provide. Providers derive internal resource IDs from names when the
backing platform needs a typed path, such as `application:api`.
`DisplayName` is an optional presentation label for readability in Resource
Manager and other presentation surfaces. Display-name editing is a future
Resource Manager capability; it should update only the presentation label and
must not change the resource ID, resource name, type, provider identity,
dependencies, permissions, or other stable references.

Not every resource exposes lifecycle status. Runtime resources such as
applications, container hosts, containers, configuration services, secrets
vaults, load balancers with runtime providers, and other managed services can
produce `State`. Logical model resources such as DNS zones and DNS name
mappings can omit `State` entirely because they describe configuration,
relationships, or materialization intent rather than a running process or
service. `null` state means "no lifecycle status is produced"; it is not the
same as `Unknown`, which means the resource participates in lifecycle status
but the provider cannot currently determine the value.

A storage volume resource represents allocated physical storage that can be
referenced and mounted by another resource. A simple local volume can be
declared without a separate storage-provider resource for local development,
while provider-backed storage can later own materialization, diagnostics, and
usage metrics for specific volume types.

A container app resource is the top-level deployable workload. It may be bound
to a specific container host resource, such as Local Docker, or it may omit that
binding and let CloudShell resolve the configured default container host. That
host selection is deployment plumbing; consumers should not need to know which
runtime type or runtime container is used to deploy the app. A container app is more
than a single container: it may be backed by one or more runtime container
instances/replicas, and those runtime instances may change across deployments.
Docker and other host providers may also project runtime container resources,
often as children under a container host resource. Projected resources can
still have stable, addressable resource IDs when the provider has a stable
underlying identity. For Docker containers, the resource ID is derived from
the host resource ID plus the actual container identity. Those runtime
container resources are useful for inspection and low-level operations, but
they are not the stable deployment target for app image updates.

Provider-owned resources may create and manage runtime containers without
becoming container app resources. For example, a load balancer provider can run
Traefik in a container, track that container as provider-owned runtime state or
as a child resource, and tie its lifetime to the load balancer resource. The
stable resource remains the load balancer; the container is implementation
detail unless the user explicitly models it as a workload they want to manage.

When provider-owned infrastructure needs placement, CloudShell should select a
host resource rather than a container engine. In this context, a host is an
instance of a runtime or control boundary that CloudShell can target, not
necessarily a physical machine. A host may represent Docker, Podman,
containerd, Kubernetes, systemd, a VM boundary, a scheduler, or a vendor
appliance API through capabilities and provider-owned attributes. The stable
resource records the selected host; the provider decides how to use that host's
runtime capabilities.

Use "container host" for the selectable CloudShell resource or configured
runtime instance. Use "container runtime" for the implementation capability or
product family behind that host, such as Docker, Podman, containerd, or CRI-O.
Avoid "engine" as a CloudShell abstraction except when naming a specific product
concept such as Docker Engine or preserving compatibility with older APIs.

Container app deployments create app-owned revisions. The current revision is
projected on the container app resource and changes when the deployable image
is updated. Runtime container instances/replicas are implementations of a
revision; they are not themselves the revision identity.

Resources can now also project ownership metadata that distinguishes who
created them, who manages them, how visible they should be in normal Resource
Manager inventory, which stable resource owns them, and how they should be
cleaned up with that owner. This metadata does not split CloudShell into
separate resource graphs. The resource graph remains unified; visibility and
permissions only decide which parts of that graph are shown in which contexts.

Some resources are hidden from the global inventory by default but still
belong to the visible resource graph when the user has permission. A
storage-owned volume or a container-app replica can be shown by Resource
Manager on the parent resource page, in relationship views, or in selectors
without appearing as a top-level inventory item. That placement is a UI
presentation concern; the domain model only records ownership, visibility,
management mode, and permissions. These resources can also be hidden by
permission when an environment does not allow a user to inspect that part of
the graph.

Internal managed resources are stricter. They are provider, orchestrator, or
runtime implementation details for another resource and should never be part
of the default user-facing graph. They may be exposed only through explicit
diagnostic or advanced inspection modes, when the user has the required
permission. For example, a provider-owned helper container can stay internal
even though CloudShell may still track it for cleanup, diagnostics, and
relationship integrity. The stable user-facing resource remains the
application, storage resource, load balancer, or other modeled resource that
owns the behavior.

Orchestrators materialize a container app by producing an orchestration-level
runtime service descriptor. In CloudShell's orchestration contracts this is
represented by `ResourceOrchestratorService`: a provider-facing descriptor
derived from the stable resource id, workload configuration, desired replica
count, ports, networks, and dependencies. A container app produces one of these
descriptors today. It is the grouping used to keep track of the runtime
implementation for the service contained by the resource: replicas, endpoint
bindings, dependency ordering, network membership, and related provider-owned
runtime services such as app ingress. It is not a projected Resource Manager
resource by default. Docker Compose maps it to a Compose service where replicas
can be declared, Kubernetes-oriented providers can map it to
Service/Deployment-style objects, and the default local runner uses the
container app identity as the implicit service identity for convention named
replica containers.

The orchestrator deployment and revision abstractions are the shared lower
layer for applying runtime intent. A resource can still be managed directly by
Resource Manager while an orchestrator derives a default deployment for a
deployment-relevant state or configuration change. They are available for
internal container-app, provider, and orchestrator implementation work before
they are announced as a public management surface. A container app revision
answers application-version questions; an orchestrator deployment/revision
answers what runtime workload was applied and which service/runtime resources
resulted.

The container app resource is also the normal user-facing deployment and
exposure artifact for application workloads. It can own the stable application
endpoint, desired replica count, discovery name, public exposure intent,
ingress or load-balancer mapping, DNS/name mapping, and health/routing
diagnostics. Provider-native service objects, such as Kubernetes Services,
Docker Compose services, or local runtime service descriptors, are
materialization details unless a provider explicitly imports or projects them.

This is distinct from the optional `cloudshell.service` resource type at the
CloudShell model and API layer. A `cloudshell.service` resource can still model
a logical service unit or facade over non-application targets, multiple
application targets, imported provider-native services, or advanced routing
scenarios that need a stable discovery name independent of one container app
lifecycle. One potential use is a manually composed replica set: several web
application instance resources can be grouped behind a shared service-resource
frontend, then a load balancer can target that service resource's endpoint
instead of each instance. It is not required to expose a normal container app
in the MVP, and it is not the internal orchestrator service descriptor by
default. Later orchestrators may deliberately materialize a
`cloudshell.service` resource as their own service-resource primitive, or
derive an orchestrator service descriptor from it, when that resource
represents the service unit that should be scheduled, discovered, or exposed
together.

`Attributes` are not a second provider configuration schema. They are projected
facts useful for inspection, filtering, diagnostics, and orchestration hints,
such as container image, workload kind, endpoint count, service port count, or
configuration entry count. Providers must not expose secrets through resource
attributes.

Attribute values are strings for the MVP. This keeps the API projection,
generated details UI, programmatic declarations, and provider implementations
simple and stable. Use invariant formatting for numbers, lower-case strings for
booleans, and stable non-localized tokens for enum-like values. If a value needs
structure, lifecycle semantics, validation, or secrecy, it belongs in
provider-owned configuration or runtime state instead of `Resource.Attributes`.

Attribute names use dotted lower-camel segments such as `workload.kind`,
`container.image`, `container.registry`, and `configuration.entries`. Names in
`ResourceAttributeNames` are reserved for CloudShell-defined meanings. Provider
or extension-specific attributes should use a stable provider or domain prefix,
for example `acme.cluster` or `postgres.database`. Do not use display labels as
attribute names; generated details can format the name for presentation.

Because `Resource` is uniform, the shell can generate a default detail view from
the projected resource shape: identity, class, endpoints, attributes,
dependencies, health checks, actions, and observability. Provider-owned detail
routes, resource tabs, or update components override that default when a
resource needs a specialized operational experience.

Consumers can filter resource lists by `ResourceClass` when they need broad
class-level views, such as all container-backed resources or all logical
services, without relying on provider-specific `TypeId` values.

`IResourceManager` publishes `ResourcesChanged` notifications after
resource-manager mutations such as create, registration changes, dependency
updates, deletion, resource actions, and image updates. The notification is a
coarse inventory signal, not a replacement for re-reading resources. Consumers
that need current state should reload the relevant resource or resource list
when notified. Provider-discovered external changes, such as a Docker
container appearing outside CloudShell, still require provider polling or a
future provider push channel unless the provider itself raises a resource
manager mutation.

`ResourceClass` describes the projected domain shape, not the provider's
internal runtime mechanics. For example, an ASP.NET Core project resource can be
process-backed and still project as `ResourceClass.Project` with project-shaped
attributes rather than executable command attributes.

For known resource types, `ResourceClass` is part of the resource model
invariant. The class declared by the resource type, creation metadata,
programmatic declaration metadata, and provider projection should agree.
Resource Manager validates this at creation and projection boundaries. Invalid
creation metadata is rejected before provider dispatch; invalid provider or
declaration projections are reported through resource model diagnostics and the
projected `Resource` is normalized back to the known type class so consumers do
not receive an invalid model.

As a client API entity, `Resource` should be convenient to inspect without
becoming an active service object. It may expose domain helpers such as
case-insensitive resource-action lookup and standard lifecycle action
properties. It should not execute operations itself. Commands still go through
`IResourceManager`, which can represent either the in-process Control Plane or a
remote API-backed adapter.

Capabilities describe what role a resource can play; they do not make the
resource an active service object. A workload that exposes endpoints can
advertise `endpoint.source`. A resource that supports configured environment
variables can advertise `environment.variables`. A managed network, reverse
proxy, load balancer, or containerized network controller can advertise
networking capabilities such as `networking.provider`,
`networking.endpointProvider`, `networking.endpointMapper`,
`networking.gateway`, `networking.loadBalancer`, or
`networking.serviceDiscovery`. The Control Plane still mediates operations,
authorization, audit, and remote access. See [Resource capabilities](capabilities.md)
for the common capability vocabulary.

### Resource type

A resource type identifies a kind of resource and, when appropriate, the UI for
adding or updating that resource.

Resource types are extension contributions. They are user-facing and stable
across providers and hosts.

Examples:

- `docker.host`
- `application.executable`
- `configuration.store`
- `secrets.vault`
- `cloudshell.network`
- `cloudshell.service`

Resource type registration is separate from resource discovery. A provider can
discover available resources, while a resource type contribution describes how a
user can add or configure a resource of that type.

Resource type contributions can also declare the expected `ResourceClass` for
that type. This class is a model constraint, not a UI hint.

Resource metadata should follow an inheritance model:

```text
Base resource model -> ResourceClass -> resource type/kind -> resource instance
```

Base resource metadata applies to every resource. `ResourceClass` metadata
describes portable class-level concerns, such as storage-capable resources or
network-capable resources. Resource type or kind metadata refines that class
for a specific user-facing type, such as `application.aspnet-core-project` or
`application.sql-server`. Resource instance configuration is the final layer
and can override or materialize the inherited defaults when the provider and
environment allow it.

Endpoint descriptors are one example of this model. A descriptor announces the
service a resource type can expose by default, such as endpoint name, protocol,
and target port. The provider that contributes the resource type also declares
whether it supports remapping that endpoint to a different concrete port in
topologies where that is useful. The descriptor does not itself bind a host
address. The provider, network, or runtime uses the descriptor plus instance
configuration to create concrete endpoint assignments and mappings. Attributes
and capabilities should follow the same inheritance model as their contracts
become explicit: base, class, type/kind, then instance.

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

Control Plane resource provider registration is separate from CloudShell UI
integration registration. The same extension or NuGet package may provide both,
and most user-facing resource providers should, but the contracts target
different apps. The Control Plane provider projects and operates resources.
The UI integration contributes resource type registration components, update
components, tabs, detail routes, and UI actions for Resource Manager. This
separation matters in split hosting, where the shell and Control Plane may run
as different processes even when the common development host runs them together
inside one ASP.NET Core application.

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

Programmatic declaration startup and dependency startup are separate policies.
`WithAutoStart(...)` expresses whether a declared resource should start when the
Control Plane starts. `WithDependencyAutoStart(...)` expresses whether that
resource may be started automatically when another resource starts with
dependency startup enabled. Explicit declaration overrides win over provider
defaults, and provider defaults win over graph-level defaults.

Resources created through the UI are not startup-autostart resources. Create
flows use an explicit "start after create" option, with the initial value coming
from provider policy, and the create operation must request that behavior
explicitly.

### Endpoint and networking

Endpoint descriptors describe the services a resource can expose before a
concrete address exists. A `ResourceEndpointDescriptor` belongs to the resource
type, kind, or instance contract and includes a stable endpoint name, protocol,
target port, default exposure, assignment default, and whether the provider can
remap that endpoint to a different concrete port in a given topology.

Resource endpoints are projected resource facts. They describe the named
endpoint contract on a resource instance: endpoint name, protocol, target port,
and explicit `ResourceExposureScope`. They do not carry concrete addresses;
topology-specific reachability is projected through
`ResourceEndpointNetworkMapping`.

Endpoint requests are networking intent. They describe what should be assigned
or reserved, including protocol, host or IP address, port, exposure scope, and
assignment mode. Manual assignments require the caller to provide the concrete
address details. Auto or provider-default assignments let a networking provider
resource choose an address from its configured policy. Endpoint requests are
resolved against endpoint descriptors by a network, runtime, or provider.

Endpoint network mappings connect a resource endpoint to a topology and provide
the current resolved address for that topology. Resources project these
topology-specific addresses through `Resource.ResourceEndpointNetworkMappings`.
For local development, an Aspire-compatible helper such as
`WithHttpEndpoint(port: 6000)` declares an HTTP endpoint descriptor and creates
assignment intent for the implied local network; the resulting network mapping
is the address the provider passes to the service when it starts.

Configured endpoint mappings connect a source endpoint to a target endpoint. A
mapping may be realized by the same network resource that owns the source
endpoint, or by a specialized networking provider resource such as a gateway,
load balancer, service discovery system, or custom controller running as a
managed resource.

Network resources project configured endpoint mappings through
`Resource.ResourceEndpointMappings`. A configured mapping is a resource
relationship in the resource model, not a provider-specific attribute.
API-backed clients receive the same source endpoint, target endpoint, network
reference, and provider resource reference as in-process consumers. The
Resource Manager uses that projection to show mappings on network resources and
read-only network exposure on target resources.

The configured mapping records both the logical network boundary and the
provider resource that should materialize or validate the mapping. The provider
resource must advertise `networking.endpointMapper`; resources that assign or
reserve endpoints advertise `networking.endpointProvider`.

CloudShell uses three basic network resource kinds:

- Host network: the implicit default when no network resource has been created.
  The default Control Plane projects it as the local host environment.
- Logical network: a named CloudShell boundary for endpoint requests and
  configured endpoint mappings.
- Virtual network: a richer environment boundary intended for on-premise or
  provider-backed infrastructure, including ingresses, gateways, backend pools,
  clusters, and load balancers.

The built-in `cloudshell.network` resource represents host or logical network
boundaries. The built-in `cloudshell.virtualNetwork` resource represents a
virtual network boundary using the same endpoint request and configured
endpoint mapping model. For local development, the default host-local
implementation can reserve manual localhost endpoints or auto-assign stable
localhost ports from the configured range on Windows, macOS, and Linux. Richer
network topology, routing, policy, TLS, DNS, clustering, and load-balancing
behavior should be expressed as capabilities on authored resources and
implemented by provider-owned configuration behind those resources.

When a virtual network is projected by the default host-local implementation
without external mapping providers, it carries
`network.hostReadiness=logicalOnly`. When mappings name external providers, it
uses `network.hostReadiness=providerRequired` and
`network.mappingProviders` to name the provider resources. The shell can use
those projected facts to warn that real virtual-network configuration requires
an activated host networking service such as a gateway, load balancer, DNS
publisher, service mesh, firewall manager, or cluster network controller.

The first built-in host networking provider is the portable local host
networking provider. It projects `networking:host-local` on macOS, Linux, and
Windows and can materialize HTTP, HTTPS, and TCP configured endpoint mappings
as local TCP proxies. OS-native providers can later materialize the same
configured endpoint mapping model through Linux, Windows, macOS, or
runtime-specific networking facilities.

`network:host` and `networking:host-local` are intentionally separate. The
former is the default topology boundary; the latter is the provider resource
that can materialize local proxies, host publishing, and other host-local
mapping behavior.

When configured endpoint mappings are declared, the network resource exposes a
reconcile action. The Control Plane action validates that the source endpoint
exists, the target endpoint exists, and the selected provider resource
advertises endpoint mapping capability. Provider-owned controllers can then use
their own resource configuration and actions to apply routing, DNS,
load-balancing, policy, TLS, or other runtime-specific behavior.

### Resource action

A resource action is a domain operation on a resource.

Standard lifecycle actions use `ResourceActionKind`:

- `Start`
- `Stop`
- `Pause`
- `Restart`

Providers can also expose custom actions with stable IDs.

Resource actions are not UI actions. A UI button or menu item may render a
resource action, but the UI element is only a presentation of the resource
operation. A UI action is custom shell behavior attached to a resource in the
UI. It may invoke a resource action, navigate to an extension-owned view, open
a configuration workflow, or perform another UI-only operation. UI actions are
registered by the UI resource provider or extension; resource actions are
projected by the Control Plane provider through the resource model.

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
lifecycle operations, such as start, stop, pause, and restart. These helpers
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

Container app providers should surface console logs from the underlying
containers or local processes. Console logs represent stdout/stderr emitted by
the workload and are resource-type-specific operational detail.

Every visible resource should also expose a platform-owned `Resource events`
stream. Resource events are actor-attributed audit-style records for operations
performed on a resource, such as executing an action, changing configuration, or
updating a deployable image. Provider or resource-type-specific logs can add
more detail, such as container console output or container-app-specific restart
events, but generic resource events are the consistent per-resource history.
They are queryable activity records, not just text log lines. Resource Manager
presents this stream as Activity, while provider resource logs remain separate
operational streams.

Operational severity is a resource-domain concept. CloudShell uses
`ResourceSignalSeverity` values (`Success`, `Info`, `Warning`, and `Error`)
for resource diagnostics, current failure summaries, and UI callouts. Resource
events use typed severity in the domain model, and API/persistence projections
serialize that severity as stable strings. Hard lifecycle/action failures
should be `Error`; warning-level dependency or startup conditions remain
`Warning` when the requested action can continue or when the warning is
advisory.

Standard resource actions and standard resource events are related but
separate concepts. `ResourceActionIds` names standard operations such as
`start`, `stop`, `pause`, and `restart`. `ResourceEventTypes` names standard
activity facts such as `action.lifecycle.stop`, `event.lifecycle.stopping`, and
`event.lifecycle.stopped`. Authors may still define custom resource actions and
custom resource event types. Custom action event types use
`action.<custom-action-id>`, and authors may namespace their own action IDs,
for example `action.database.backup`. Authors may also namespace their own
event types under `event.*`, such as `event.database.backup.completed`; only
the standard lifecycle action kinds receive Resource Manager lifecycle events
automatically.

### Action and Event Naming

Action IDs name requested operations. Standard lifecycle actions use simple
stable IDs: `start`, `stop`, `pause`, and `restart`. Custom actions may use
namespaced IDs such as `database.backup` or
`loadBalancer.applyConfiguration` when the namespace improves clarity.

Action event types record that an action was requested. Standard lifecycle
action event types use `action.lifecycle.*`, for example
`action.lifecycle.start` and `action.lifecycle.stop`. Custom action event
types use `action.<custom-action-id>`, preserving author namespaces.

Event types describe facts that happened. Standard lifecycle events use
`event.lifecycle.*`, for example `event.lifecycle.starting`,
`event.lifecycle.started`, `event.lifecycle.stopping`, and
`event.lifecycle.stopped`. Standard deployment events use
`event.deployment.*`, for example `event.deployment.image.updated` and
`event.deployment.replicas.updated`. Authors may define their own event
namespaces under `event.*`, such as `event.database.backup.completed`.
Provider-scoped activity events use `event.provider.<provider-id>.*` and are
attached to the resource whose procedure the provider is fulfilling. They let
provider implementations record concise procedure milestones, such as DNS
settings being published or a container replica being started, without turning
those details into standardized lifecycle events.

Display names are presentation metadata. The Activity UI can show friendly
labels such as "Start action" or "Started" while preserving the stable event
type as metadata.

In code:

- `ILogManager` is the public domain abstraction.
- `ILogStore` is the internal Control Plane implementation store.
- `ILogProvider` is the provider contract for contributing and accessing log
  sources.
- `ResourceLogSource` is the resource-model declaration for a log source.
- `LogSource` is the Control Plane projection used for listing, authorization,
  reading, querying, streaming, parsing, and rendering.
- `IResourceEventManager` is the public domain abstraction for resource
  activity queries.
- `IResourceEventStore` is the internal append/query store for resource
  events.

### Template

A resource group template is a portable group-level envelope owned by
CloudShell. Individual resource payloads inside the template are provider-owned.

In code:

- `IResourceTemplateManager` is the public domain abstraction.
- `ResourceTemplateService` orchestrates import/export.
- `IResourceTemplateProvider` owns per-resource import/export behavior.

This preserves the ownership split: CloudShell owns grouping and orchestration;
providers own their resource configuration schema.

Template import follows the same validation posture as resource creation:
expected invalid states are represented as diagnostics on the import result.
Invalid template envelopes, such as unsupported kinds or versions, do not create
resource groups and do not throw from the domain import API.

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
- `UpdateResourceImageCommand`
- `UpdateResourceReplicasCommand`

The HTTP API maps these commands to routes and request DTOs. Consumers should
prefer the manager abstraction unless they are implementing or generating an
HTTP adapter.

`UpdateResourceImageCommand` targets the top-level resource that owns the
deployable image, such as a container app. It should not require consumers to
target provider-specific runtime children such as Docker containers. In the HTTP
projection this is exposed through the Container Apps API, for example creating
a revision for a container app, rather than as a resource-type-specific route
under the core Resource Manager `/resources` endpoints.

`UpdateResourceReplicasCommand` targets the same stable container app resource
and updates the explicit desired replica count. Runtime containers created for
replicas remain provider-owned implementation instances, not API targets for
deployment automation.

The intended build-server deployment procedure is to push an immutable image tag
to a registry, then call the authenticated Container Apps revision endpoint with
that tag. The Control Plane authorizes the caller, updates the app-owned image
configuration, creates a new revision, and records a resource event with the
actor or external trigger supplied by the build action.

`CreateResourceCommand` may carry `ResourceClass` and stable, non-secret
attributes from the selected resource type or caller context. Creation providers
receive that metadata through `ResourceCreationRequest`, but provider-owned
configuration and runtime state remain in the request configuration payload and
provider stores.

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
