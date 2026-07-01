# Architecture Decision Log

This document records durable CloudShell product and architecture decisions.
It is intentionally separate from [Changelog](CHANGELOG.md), which records
landed implementation changes, and [Roadmap](docs/roadmap.md), which owns
milestone scope and task ordering.

Decision IDs are stable enough to reference from changelog entries and related
docs. When an implementation change follows a decision, the changelog should
link to the decision so the dependency is visible.

## 2026-07-01

### ADR-20260701-001: Keep Control Plane application defaults separate from UI shell registration

CloudShell's Aspire-like application setup should produce an opinionated
Control Plane application by default, not a combined UI and Control Plane
host. The Control Plane owns the runtime/resource-management surface and can
register built-in Resource Model provider defaults through an application
preset such as `AddCloudShellControlPlaneApplication(...)`.

The CloudShell UI remains an explicit shell surface. Hosts that want the UI in
the same ASP.NET Core process should call `AddCloudShellUi(...)` separately.
UI extensions, Resource Manager UI extensions, and provider-owned Resource
Manager views belong in the UI registration callback so UI composition stays
separate from Control Plane provider/runtime registration.

`AddCloudShell()` remains a convenience for the combined development host, but
the architectural split is Control Plane first, UI opt-in.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-30

### ADR-20260630-001: Keep host-level authoring context outside resource templates

CloudShell should keep `ResourceGraphBuilder` focused on resources
and templates. Host-level capabilities that can influence resource construction,
such as identity-provider registration and default identity-provider lookup,
belong to the Control Plane authoring context used by host registration
methods such as `DefineResources(...)` and `DefineInitialTemplate(...)`.

That context may extend or wrap the graph builder so provider-owned resource
builder methods remain available, but metadata such as identity providers is
not emitted as `ResourceDefinition` entries and is not part of
`ResourceTemplate` interchange. The Control Plane context copies that metadata
to Control Plane services, such as the identity-provider catalog, when the host
registers the resources.

Resource defaults that are themselves resources, such as the Host network or
default container host, can remain lazy `ResourceGraphBuilder` accessors because they
represent realized resources in the environment. Identity-provider defaults
stay outside resource templates until CloudShell deliberately introduces a resource type
for identity providers.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-29

### ADR-20260629-001: Name the Resource model and Runtime model

CloudShell should use **Host Environment** for the managed environment where
the complete realized model exists. The term describes the environment that
CloudShell manages, not the ASP.NET Core web host process.

CloudShell should use **Resource model** for the resource-focused subset of
the realized model. It contains resources, dependencies, endpoints, and
endpoint mappings or names. This is the model most users need when asking what
resources run in an environment and how they connect. Its graph representation
is the **Resource graph**.

CloudShell should use **Runtime model** for the fuller management and
orchestration model of the same host environment. The Runtime model includes
the Resource model as a subset and adds environment artifacts: orchestration
services, replica groups, replicas, routing bindings, retained or superseded
runtime revisions, and environment revisions. Its graph representation is the
**Environment Map** or runtime graph.

Resources, services, replica groups, replicas, and routing bindings are
environment artifacts in the Runtime model. They are not only deployment
internals. A deployment definition may define, update, replace, or retire
those artifacts, and an environment revision records the versioned outcome of
that realization.

Earlier draft terms such as **Host Environment Model**, **Environment Resource
Model**, and **Environment Runtime Model** are deprecated aliases for the
realized model, Resource model, and Runtime model respectively.

The canonical vocabulary lives in
[CloudShell Terminology](docs/terminology.md). Domain, resource, architecture,
proposal, and roadmap docs should link to that document instead of redefining
shared terms locally.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260629-002: Keep provider runtime integration behind adapter contracts

Resource model providers are extension packages. They own resource
semantics: type IDs, accepted attributes, validation, graph projection,
resource actions, and provider-owned operation descriptions. They should not
depend directly on host/runtime implementations such as local process
launching, Docker command execution, filesystem materialization, network
reconciliation, sidecar process hosting, or orchestrator controllers.

Runtime integration flows through adapter contracts shared by the provider
package and the host or default runtime integration package. A provider invokes
an interface that represents the provider-owned operation it needs, while the
host/runtime package registers the concrete implementation. This preserves the
extension dependency direction: providers register themselves and declare what
runtime capabilities they can use; the hosting environment decides which
concrete adapters are available.

Default reference implementations may live beside reference providers while
the Resource Model POC is being migrated. When those implementations become
host-shaped or reusable across providers, they should move behind a default
runtime integration package rather than becoming direct dependencies from
providers to the host. Missing adapters should surface clear diagnostics or
unavailable actions instead of causing providers to silently reach across the
runtime boundary.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-24

### ADR-20260624-001: Prove resource definitions in an isolated experimental project

CloudShell should prove the formal resource-definition model in a separate
`CloudShell.ResourceModel` project before moving the contracts into the
public `CloudShell.Abstractions` surface or integrating them into the Control
Plane pipeline.

The project is an experimental implementation boundary for
`ResourceDefinition`, `ResourceClassDefinition`, `ResourceTypeDefinition`,
resolved attributes, resolved capabilities, resolved operations, diagnostics,
attribute validators, and attached capability/operation provider contracts.
This lets the proposal's API shape be exercised by focused tests while the
model is still allowed to change.

The POC does not change current resource declarations, provider-specific
definition stores, Resource Manager persistence, Control Plane API contracts,
remote clients, or provider lifecycle behavior. Those integrations remain
future slices once the model proves useful and stable enough to graduate.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-21

### ADR-20260621-001: Design Control Plane scale-out around a primary controller and workers

CloudShell should eventually support Control Plane scale-out for shared
on-premise environments. API-facing Control Plane hosts should be able to run
as replicas behind a load balancer, while singleton duties such as lifecycle
reconciliation, resource-state convergence, lease-sensitive provider actions,
and scheduled polling run through a primary controller role.

The primary controller role should not be tied to a specific process forever.
It should be coordinated through a durable lease, leader election, or
equivalent store-backed ownership mechanism so a different Control Plane
process can take over after failure. Control Plane APIs must remain the
authorization and validation boundary regardless of which process currently
owns controller duties.

Subsystems that do not need to live in the request-serving API process should
be candidates for independent worker processes. Examples include log-source
readers, log persistence, telemetry ingestion, health polling, notification
fan-out, and provider reconciliation. These workers should consume explicit
work, leases, subscriptions, or source assignments rather than each API
replica independently polling or streaming the same external source.

Resource Manager should continue to present one coherent Control Plane even
when the backing deployment is split into API replicas, a primary controller,
and background workers. The implementation must preserve resource-level
authorization, resource/event correlation, auditability, and provider
ownership boundaries.

This is future scale-out direction. The local-development MVP can keep the
combined in-process host, but new Control Plane subsystems should avoid
assuming that all stateful background work runs inside every API host.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-19

### ADR-20260619-006: Gate observability by signal permissions and resource read access

Observability is a separate product area from resource inspection and resource
operation. CloudShell uses a grouped `observability.read` permission for the
Telemetry workspace plus signal-specific permissions for logs, traces, and
metrics: `observability.logs.read`, `observability.traces.read`, and
`observability.metrics.read`.

The grouped permission allows all observability signal views. Signal-specific
permissions allow hosts and administrators to grant logs, traces, or metrics
independently while still keeping the views grouped under Observability in the
shell.

Observability permissions do not override resource access. Control Plane
telemetry queries must filter log descriptors, trace spans, and metric points
to resources where the caller has read-level resource access. Resources the
caller cannot read are not listed in observability data. Reference-level
resource access can later support locked topology nodes, but detailed logs,
spans, metrics, attributes, endpoints, and trace rows require resource read
access.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260619-005: Model resource access as ordered effective levels

CloudShell authorization distinguishes whether a caller can discover a
resource as a graph reference, inspect it, operate it, or manage it. This is
modeled as an ordered `ResourceAccessLevel`: `None`, `Reference`, `Read`,
`Operate`, and `Manage`.

`Reference` is a first-class access level, not a UI workaround. It allows a
resource to appear as a locked or redacted node when it is needed to explain an
authorized resource, dependency, topology edge, trace, health rollup, or other
relationship, without exposing resource details, telemetry attributes,
endpoints, configuration, logs, health details, or actions. `Read` allows
inspection. `Operate` allows resource operations and therefore includes
inspection. `Manage` allows administrative management and remains the
compatibility superset for resource actions.

Provider-declared `ResourceVisibility` remains separate. Visibility describes
the resource's default graph/display behavior, such as normal, hidden, or
diagnostic. `ResourceAccessLevel` describes the current caller's effective
authorization. The Control Plane should compute and enforce effective access at
manager/API boundaries; UI components should render the resulting state and may
show locked references where the caller has only `Reference`.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260619-004: Store resource health as Control Plane snapshots

Resources declare health checks as resource-model intent, but the Control
Plane owns executing those checks and storing observed health snapshots. UI
hosts and other clients must read the latest Control Plane health state instead
of probing resource endpoints directly. This keeps browser sessions and split
UI hosts from multiplying probe load against managed services.

Liveness is an observation; health is an assessment. A liveness probe answers
whether a resource or runtime scope appears alive enough for lifecycle and
recovery policy. A health result may assess liveness together with readiness,
dependency state, provider-owned status, and aggregate endpoint data exposed
by the application or provider.

The latest health state is an operational cache and should always be available
when resources declare checks. Historical health snapshot retention is separate
and should be opt-in for local development, because short-lived local sessions
often need current state without accumulating history. Health state is retained
through a store abstraction so the local development host can start with a
latest-state cache and optional bounded in-memory history while database-backed
persistence can retain health history across restarts. Hosts opt into retained
history through appsettings under `ResourceManager:Health`, including the
snapshot store and retained snapshot count. Database-backed snapshots use the
Resource Manager persistence database configured under `Persistence`.

This follows the same direction as logs, traces, metrics, and resource events:
operational data is collected or ingested into the Control Plane, then queried
by UI and API clients.

Historical health snapshots are useful beyond the Health workspace. They should
be queryable for future correlation with traces, logs, metrics, lifecycle
events, and larger environment snapshots that summarize state across several
operational areas.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260619-003: Split built-in application resources by provider boundary

The built-in application capability is a package of resource providers, not a
single provider that owns every application-shaped resource. Executable
applications, ASP.NET Core projects, container apps, and SQL Server each have
their own provider identity for registration, lifecycle dispatch, templates,
programmatic declarations, orchestration descriptors, and future resource model
evolution.

These providers may share internal application infrastructure for common
runtime concerns such as local process execution, container-backed startup,
environment variables, endpoint projection, logs, monitoring, templates, and
orchestration. Shared infrastructure should not reintroduce an active aggregate
provider boundary. The legacy `applications` provider id is compatibility
metadata for old templates and declarations, not a registered built-in provider.

Provider capability interfaces should also follow the resource boundary.
Shared infrastructure may implement reusable operations internally, but only
the provider whose resource type owns a capability should advertise that facet
through interfaces such as image update, replica update, or orchestrator
service procedures.

The shared application service is infrastructure, not a provider. It may expose
methods used by built-in providers, but it should not implement provider-facing
facet interfaces directly.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260619-002: Make CloudShell UI a generic extensible shell

CloudShell UI should evolve into an independently useful shell platform, not a
Resource Manager-specific application. Resource Manager remains the most
important built-in shell extension and the primary MVP proof, but the shell
should own generic composition primitives that Resource Manager and other
extensions can share.

The composition engine is a reusable shell-structure substrate, not a
mechanism that can only run inside CloudShell UI. It stores low-level
structure and relationships between content. CoreShell uses it below its
framework-neutral UI extension services as a CMS-like toolkit for building
shells, and presenter or sandbox hosts can use the same model for pages and
layouts when they need dynamic composition. Shell integrations should target
CoreShell services rather than the lower-level composition graph directly.
Domain shells such as CloudShell can build their own domain-focused extension
points on top of CoreShell.

The first implementation proof should split the composition surface into two
libraries: `CoreShell.Composition` for framework-neutral core facilities
such as typed IDs, graph registration, validation, and target resolution, and
`CoreShell.Composition.Blazor` for plain Blazor components and routing
context integration. The Blazor components should not depend on Fluent UI,
Bootstrap component packages, CloudShell Hosting, Resource Manager, or the
CloudShell extension model. Host applications can style the plain markup with
ordinary CSS or build their own renderers over the core library.

CloudShell extension integration should be layered on after the standalone
composition structure is credible. The extension adapter maps CloudShell
extension contributions into the core composition graph; it should not make
the core graph depend on CloudShell extension contracts.

Persisting parts of the composition graph can become a later CMS-like
capability, but persistence should target durable core graph metadata rather
than Blazor components. Component types, service wiring, capability checks,
and executable UI remain code-owned integration concerns.

CloudShell should eventually host the composition root from the core shell
main layout so navigation, topbar services, notifications, routed pages, and
nested outlets share the same resolved composition context. That lets
integrating services target shell-provided IDs without each page wiring the
composition engine independently.

The target shell model is a validated layout/content graph with CMS-like
dynamic composition, not a menu or tab API. Navigation hierarchy and content
hierarchy are separate. Menu nodes describe menus, menu item groups, menu
items, and sub-menu items. Content nodes describe addressable pages,
sub-pages, slots, section containers, and sections. A menu item can target
content by ID without owning the content hierarchy. It should generalize the
reusable layout ideas already present in shell-hosted views and Resource
Manager tabs: grouped local navigation, selected page state in the URL,
dynamic component hosting, ordered sections, context injection, and
renderer-owned presentation. Extensions contribute components, metadata,
labels, icons, capability declarations, layout IDs, and calls to public domain
managers. The host owns layout graph validation, ID-based link and deep-link
resolution from registered content route metadata, ordering, accessibility,
startup validation, permission-aware visibility, and conflict handling. Razor
and Blazor still own route matching through normal routing mechanisms such as
the `@page` directive.

This keeps Resource Manager UI extensions resource-specific while avoiding a
Resource Manager-only shell. Resource Manager should adapt its tab, predefined
view, and section contracts onto generic shell composition primitives rather
than making `ResourceTabContribution` the generic shell API. A provider can
contribute resource tabs and sections through Resource Manager contracts, and
can also contribute broader shell pages, settings, notifications, or dashboard
content through generic shell contracts.

For MVP, broad shell composition is deferred behind local-development
convergence. Resource Manager changes should still avoid page-specific
shortcuts that would block the future composition model.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260619-001: Keep built-in identity persistence separate from Resource Manager persistence

CloudShell uses two persistent stores when the built-in identity provider is
database-backed: the Resource Manager database and the built-in identity
database. The Resource Manager database owns platform and Control Plane state
such as resource registrations, groups, dependencies, declaration persistence,
grant intent, activity, events, and provider-owned platform records. The
built-in identity database owns provider state such as users, password hashes,
roles, claims, tokens, and ASP.NET Core Identity records.

This mirrors the boundary that external identity providers already impose.
Keycloak, Entra ID, Active Directory, and other providers own principals and
credentials outside the Resource Manager database. The built-in provider may
run in-process for local development and simple on-premise hosting, but it is
still an identity provider and should keep its persistence isolated.

Resource Manager stores identity provider registrations and desired
access-control grant intent. Identity providers apply or reconcile that intent
through provider hooks. Resource Manager must not silently reuse its database
for built-in identity persistence. Local development can default to two SQLite
files; on-premise hosting can use two SQL Server databases, preferably with
separate credentials.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-18

### ADR-20260618-002: Model access control as principal-to-resource grants

CloudShell access control follows the common IAM shape: a principal receives a
permission grant on a protected resource. Principals are actors, not resources.
A principal can be a user, group, service account, service principal, managed
identity, workload identity, resource identity, provider-owned identity
reference, or automation identity.

Resource identity bindings remain per-resource identity intent. They matter
when a resource itself should act as a principal, acquire credentials, or be
provisioned by an identity provider. They are not required for a resource to be
protected by grants. The protected resource is the target of the grant, and it
can be a configuration store, secrets vault, volume, network, application,
load balancer, or any other managed CloudShell resource.

The current Resource Manager Access control UI uses resources with identity
bindings as the first principal source because that is the principal set the
platform can resolve and provision today. This is transitional. Future user,
group, service-account, and provider-owned principal sources should feed the
same grant model and UI vocabulary instead of adding separate resource-specific
access paths.

Identity providers should integrate through provider-neutral hooks for setup,
directory queries, resource identity provisioning, access-grant reconciliation,
provisioning status, and runtime credential projection. A provider adapter can
call the backing authority directly, or it can call a provider-owned bridge
service that translates CloudShell principal and grant intent into systems such
as Microsoft Entra ID, Active Directory, OIDC/OAuth providers, RBAC
assignments, app roles, or groups.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260618-001: Use provider-neutral scopes for multi-instance telemetry

Telemetry belongs to the stable resource users manage by default, even when a
provider implements that resource with multiple runtime instances, shards,
replicas, partitions, workers, or other lower-level artifacts. Logs, traces,
and telemetry metrics should open at resource scope so users can investigate
the managed service without navigating to hidden or provider-owned
implementation resources.

When a resource has one observed telemetry scope, the resource Telemetry views
should not show a scope selector. When multiple scopes exist, Logs, Traces, and
Metrics should expose a compact selector whose default is `All instances`.
Individual options represent provider-defined telemetry scopes, not independent
management targets. A container app replica is one scope kind, but the same
abstraction should work for other providers that expose multiple resources or
runtime units as telemetry scopes.

Telemetry records should carry the stable resource identity plus optional
provider-neutral scope dimensions, such as scope resource ID, scope name, and
scope kind. Providers can add more specific dimensions such as replica ordinal,
container name, partition ID, or deployment revision. Logs use the scope to
filter or combine source output. Traces remain trace-first and service-aware:
scope filtering may narrow displayed spans, but it should not redefine the
trace as belonging only to one implementation unit. Telemetry Metrics should
default to resource-level aggregate views and allow per-scope filtering or
breakdowns. Provider-observed CPU, memory, restart count, uptime, and runtime
status remain Resource Metrics under Management > Monitoring, not Telemetry
Metrics.

Resource observability should also announce telemetry sources. A source
describes the producer or collection mechanism, such as provider-owned,
OpenTelemetry exporter, or Prometheus/OpenMetrics-style endpoint telemetry.
The shell and remote consumers can use the source metadata to understand that a
resource serves telemetry without having to know whether the data arrived
through OTLP, was scraped from a standards-based endpoint, or was supplied by a
provider. Source metadata can carry scope
descriptors so multi-replica, multi-partition, or multi-worker resources expose
their selectable telemetry units consistently across resource Telemetry tabs
and common Observability views.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-17

### ADR-20260617-001: Make container app replicas an explicit scaling mode

Container apps default to single-instance execution. A single-instance
container app can bind its own endpoint directly and does not need a load
balancer just because it is a container app.

Replicas are an explicit scaling mode, not merely the default value of a
replica-count field. Programmatic declarations opt into scaling with
`WithReplicas(...)` or a replica count greater than one. Resource Manager owns
scaling through a dedicated Application > Scale and replicas tab, where users
enable replicas, set the desired count, and inspect projected runtime replica
artifacts.

When a container app has inbound endpoints and replicas are enabled,
CloudShell must provide an ingress or load-balancer strategy so traffic can be
distributed across instances. The endpoint is still owned by the container
app: a single container binds it in single-instance mode, and an ingress or
load balancer binds it on behalf of the app in replicated mode. Worker-style
replicated apps without inbound endpoints do not require a load balancer. A
later guided Resource Manager flow should prompt users to assign or create a
load balancer/ingress provider when replicas are enabled for an
endpoint-bearing app.

Revision management remains a separate future Application view. The current
Deployment tab can show and update the current projected revision, but
rollout history, rollback, activation, and traffic splitting should not be
mixed into Scale and replicas.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-15

### ADR-20260615-004: Separate resource identity, name, and display name

CloudShell follows cloud-platform naming terminology. `Resource.Id` is the
immutable platform identity or derived resource path used by dependencies,
permissions, resource events, activity logs, provider state, API calls, and
automation. `Resource.Name` is the scoped unique resource name users and
programmatic declarations normally provide, such as `api` or `orders--api`.
`Resource.DisplayName` is an optional presentation label, such as
`Orders API`, for Resource Manager and other user-facing surfaces.

Activity logs should display or retain the resource ID as the canonical
resource address even when display names are enabled. Programmatic
registration APIs should take resource names and domain-specific parameters;
providers derive resource IDs from those names unless the caller passes an
already-qualified resource ID for advanced or compatibility scenarios.
Optional labels are applied with `WithDisplayName(...)`. Resource Manager
create flows should ask for Name first, then an optional display name when
display names are enabled.

The projected `Resource` model carries explicit `Name` and `DisplayName`
values, and Resource Manager should use `DisplayName` only as a presentation
label when display names are enabled. Resource Manager should keep the resource
ID visible in detail and overview surfaces, provide a display-name preference,
and later add display-name editing without changing the stable resource ID,
name, type, provider identity, dependencies, permissions, or other stable
references.

CloudShell does not require one global naming scheme, but teams may use
structured resource names, configuration keys, and secret names when that helps
map resource hierarchy into JSON configuration, environment variables, or
DNS-safe projections. The optional `--` separator is acceptable guidance for
hierarchy that needs to travel through systems where `:` has configuration
path meaning or is not accepted.

Character and length restrictions are provider-owned rather than global
CloudShell rules. Different backing platforms, such as Azure, AWS, local
files, DNS providers, container hosts, and future deployment providers, impose
different constraints. The built-in Configuration Store should remain broad
and App Configuration-like, while rejecting names that cannot sensibly
round-trip through text configuration. The built-in Secrets Vault should use a
Key Vault-style secret-name shape and rely on `--` for hierarchical .NET
configuration loading.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260615-003: Keep managed SQL Server distinct from container apps

SQL Server is a managed database service resource, not a generic container
application. The current `application.sql-server` implementation may continue
to use the container-backed application runtime as a transitional local
development bridge, but future SQL Server Resource Manager UX should present
database-oriented configuration and operations instead of generic container app
deployment controls such as image rollout, revisions, replicas, or app
ingress. If a provider uses a container internally, that runtime artifact is an
implementation detail or contextual diagnostic child, not the SQL Server
resource's primary management model.

`application.sql-server` should project as a managed service, not as a
container app, unless the user explicitly declares a generic container app.
Future SQL Server authoring should expose validated SQL Server concepts such as
version and edition instead of arbitrary image override APIs such as
`WithImage(...)`.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260615-002: Introduce deployment and runtime-owned resource metadata before public rollout features

Container apps need deployment, revision, and runtime-owned resource relationships to become a useful managed-service primitive. Add the shared abstractions and resource metadata first, use them internally for container apps and provider/orchestrator runtime artifacts, and keep them out of the normal public product surface until the model is proven. A container app remains the user-facing resource; orchestrator deployments/revisions and runtime-managed containers, replicas, endpoint registrations, or provider-owned artifacts are lower-level implementation and diagnostic entities that may be hidden from normal Resource Manager lists.

Hidden from global inventory does not necessarily mean internal. A child
resource such as a replica under a container app or a volume under a Storage
resource can be hidden from the top-level inventory by default while still
being part of the visible resource graph when the user has permission.
Resource Manager decides where those resources are presented, such as parent
pages, relationship views, or selectors. Internal managed artifacts are
stricter: they are provider, orchestrator, or runtime implementation details
and should never appear in the default user-facing graph.

Resources can still be handled individually by the orchestrator. When a
resource state or configuration change has runtime workload intent, the
orchestrator may derive a default deployment for that change so CloudShell can
track what was applied without requiring users to explicitly create or manage a
deployment resource.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260615-001: Separate product goal, roadmap, changelog, and ADR responsibilities

Keep the project goal, roadmap, changelog, and architecture decision log as separate documents with different responsibilities. `docs/goal.md` owns the durable product goal, `docs/roadmap.md` owns milestone scope and current task order, `CHANGELOG.md` owns dated landed changes, and `ADR.md` owns durable product and architecture decisions. Link between these documents instead of duplicating the same planning state in each one.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-14

### ADR-20260614-001: Separate host topology from installed environment capabilities

CloudShell distinguishes host topology from installed environment capabilities. A CloudShell host application is the ASP.NET Core app that hosts the CloudShell UI, the Control Plane, or both. A CloudShell environment is the managed local, team-owned, or on-premise cloud-like environment backed by Control Plane resource state, installed capability packages, and one or more UI hosts. Use capability package for NuGet-distributed installable environment capabilities, and reserve workload for runtime application execution concerns.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-002: Make container apps the primary MVP application exposure artifact

The MVP application environment path centers on container applications, app-owned exposure and discovery, virtual networks, public endpoints, load-balancer routes, and logical DNS/name mappings. Normal container app exposure should not require a `cloudshell.service` resource. Keep `cloudshell.service` optional for logical facades, imported services, non-application targets, and advanced routing until the deployment/orchestrator model is clearer.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-003: Treat storage and identity as MVP differentiators

Storage and identity are MVP differentiators from Aspire-style local orchestration. CloudShell should model volume resources and volume mappings so stateful services can be managed through Resource Manager, and it should validate the identity model against at least one third-party OIDC/OAuth provider in addition to the built-in development provider.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-004: Separate workload crash recovery from host restart recovery

Workload crash recovery is distinct from host restart recovery. Providers should project observed state when a workload crashes, while restart/no-restart/backoff policy belongs to an orchestrator layer or explicit future resource policy. Host restart recovery should reconcile resources that are bound to the host lifetime without treating every workload exit as a restart policy event.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-005: Make initial on-premise hosting the first post-MVP target

The first post-MVP target is an initial on-premise hosting scenario. It should prove acceptable Resource Manager operations, provider-backed cross-platform networking, virtual networks, ingress/public endpoint mapping, DNS/name mapping, network-level service discovery, event/integration points, and more complex validation samples.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260614-006: Grow host setup into an environment setup experience

Host setup should grow into a broader environment setup experience for platform operators. The setup flow should cover missing OS/runtime prerequisites and environment-level choices such as the default identity provider, default container host, default networking/DNS/service-discovery providers, and related readiness checks. Per-resource prompts remain useful when one resource requires a disabled or unconfigured capability.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-13

### ADR-20260613-001: Treat public exposure and API stability separately

Public exposure and API stability are separate decisions. Public APIs that are not yet stable must be labeled as preview, experimental, or unstable, with clear ownership, expected change surface, and path to stability.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260613-002: Keep logs text-compatible while adding structured metadata

Provider-owned operational logs remain text-compatible with `severity` terminology and optional structured metadata on `LogEntry`: `category`, `eventId`, `traceId`, `spanId`, `exceptionSummary`, and string-only `attributes`. Resource events, audit records, diagnostics, metrics, traces, and future non-text payloads remain separate concerns.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260613-003: Built-in services dogfood public integration surfaces

CloudShell is an open platform. Built-in services and samples should dogfood the same public integration points, identity model, service APIs, lifecycle contracts, diagnostics, and authorization surfaces that extension authors and third-party service authors use unless a documented transitional exception is needed.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260613-004: Keep ASP.NET Core project endpoints explicit

ASP.NET Core project endpoints have an explicit source order: programmatic endpoint declarations win, `launchSettings.json` is used only when `WithLaunchSettingsEndpoints()` is declared, and the provider otherwise assigns a stable local development endpoint. Resource Manager UI create/update flows remain manual and do not read launch settings.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-10

### ADR-20260610-001: Use a host-first descriptor-driven container host abstraction

The container host abstraction is host-first and descriptor-driven. Providers resolve explicit or default container hosts through a shared resolver, keep provider-owned runtime state behind provider contracts, use host-oriented public naming, and report missing host placement through action capability reasons before orchestration dispatch.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-09

### ADR-20260609-001: Model load balancing as a provider-backed resource

Load balancing should be modeled as a resource abstraction over providers. Traefik is the proposed first provider target, with routes mapped to stable resource endpoints and raw ports treated as authoring convenience.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260609-002: Provider-owned runtime infrastructure selects a host resource

Provider-owned runtime infrastructure should select a host resource, where host means an instance of a runtime or control boundary CloudShell can target. Docker, Podman, containerd, schedulers, process managers, and appliance APIs are host runtime capabilities or provider-owned facts, not separate placement primitives.

Related changes: [Changelog](CHANGELOG.md).

## 2026-06-08

### ADR-20260608-001: Consumers use domain managers instead of generated HTTP clients

Consumers should use domain managers, not generated HTTP clients directly.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260608-002: Keep Control Plane stores and providers internal

Internal Control Plane stores and providers remain implementation contracts for the service process, not public client integration contracts.

Related changes: [Changelog](CHANGELOG.md).

### ADR-20260608-003: Build servers deploy container apps by immutable image tag

Build-server container app deployments should push an immutable image tag to a registry, then call the authenticated Container Apps revision API with that tag. The Control Plane authorizes the caller, updates the image, creates the revision, and records resource events for traceability.

Related changes: [Changelog](CHANGELOG.md).
