# CloudShell architecture

CloudShell is split into application surfaces, extension surfaces, and shared
product concepts. The important boundary is not only "frontend" and
"backend"; it is which layer owns UI composition, which layer owns Control
Plane behavior, and which shared concepts an integration uses across both.

Architecture describes the overarching model and conceptual boundaries. It is
not the same as design or implementation. Design documents explain how the
architecture is expressed in APIs, UI flows, validation, and behavior.
Implementation and project-structure documents describe the concrete assemblies,
folders, components, services, and migration steps used to realize that design.
For canonical product and domain vocabulary, see
[CloudShell Terminology](terminology.md).

## Application surfaces

### CoreShell and CloudShell UI

The common shell infrastructure should be treated as **CoreShell** until a
better name is chosen. CoreShell is the product-neutral extensible shell layer:
the common layout, navigation, composition services, settings surface,
notification abstractions, extension points, and presenter contracts. It is
the CMS-like infrastructure for addressable shell content, layout structure,
and extensibility.
CloudShell UI is the product host that assembles CoreShell with the default
presenters and predefined integrations.

CloudShell UI may run inside a host application by itself, or it may run in the
same host process as the Control Plane for local development.

The hosting methods mirror that boundary. `AddCloudShellUi()`,
`UseCloudShellUiAsync()`, and `MapCloudShellUi<TRootComponent>()` are UI-only
composition points. `AddCloudShellControlPlane()`,
`UseCloudShellControlPlaneAsync()`, and `MapCloudShellControlPlane()` are
Control Plane composition points. Plain `AddCloudShell()`,
`UseCloudShellAsync()`, and `MapCloudShell<TRootComponent>()` belong to the
combined host surface and compose both sides explicitly; they should not live
in a UI-only package that can be referenced by a split CloudShell UI host.

CoreShell acts as a shell for integrations. It owns common visual structure and
shell services:

- main layout and navigation
- top bar and user/session affordances
- common Settings
- notification surfaces
- shell composition adapters and presenters
- shell-level extension areas

CoreShell should not be defined by Resource Manager. Resource Manager is the
first major built-in integration in CloudShell, but the shell must stay useful
for other product areas and extension-owned experiences.
Architecturally, Resource Manager is still an integration into CloudShell. It
is larger and more central than most extensions, but it is on the same side of
the shell boundary as another CloudShell extension. Resource Manager has a
shell extension that contributes UI and uses shell services. The backend for
that extension is the Control Plane service, which may run in the same host
process during development or remotely in a split deployment. Resource Manager
does not define the shell itself.

When the boundary matters, use precise terms: "Resource Manager shell
extension" for the CloudShell UI integration and "Resource Manager backend" or
"Control Plane side" for the backend services. "Resource Manager" by itself
can refer to the whole product area or capability package that includes both
halves.

CoreShell should also stay isolated from extensions at the implementation
level. Extensions integrate through declared extension points, shared
abstractions, and shell-provided services. They should not depend on the
concrete CloudShell UI host implementation or reach into shell internals to
participate in navigation, settings, notifications, Resource Manager views, or
other shell-owned surfaces.

The structure should not prevent another CloudShell UI implementation from
using a different UI component stack. Extension-facing contracts should be
defined through public abstractions and services first. A concrete CloudShell
UI package then adapts those contracts into its chosen rendering stack,
whether Fluent UI, plain Blazor components, or another presenter set.
An extension can still implement its own presenters and component stack for
surfaces it owns. The boundary is that shell-owned areas such as menus, pages,
sections, notifications, and settings are integrated through shell contracts
and services. From the extension's perspective those services should feel like
normal product services, such as a notification manager or layout manager,
even though the active shell decides how the result is rendered.

Fluent UI is the default CloudShell look and feel, not the shell contract.
CoreShell extension points should live in a framework-neutral layer such as
`CoreShell.Extensibility`. Fluent-specific presenters should live behind a
separate layer such as `CoreShell.FluentUI`, which CloudShell can use as its
default presenter implementation. `CoreShell.FluentUI` should expose concrete
host-usable components and integrations such as navigation menu presenters,
settings presenters, section/tab layouts, notification surfaces, and other
Fluent UI renderers over CoreShell contracts.

### Control Plane

The Control Plane is the backend application. It may be hosted with
CloudShell UI in a combined local-development host, or independently in split
and on-premise deployments.

Control Plane provider and runtime registration belongs to the Control Plane
host. UI hosts may reference UI integration packages and remote client
adapters, but they should not install provider runtime integrations unless the
host is deliberately the combined CloudShell host.

The Control Plane owns backend state and operations:

- resource inventory and registration
- resource groups, dependencies, relationships, and source metadata
- lifecycle procedures and provider orchestration
- activity, logs, traces, metrics, and operational data
- persistence integration
- API projection and remote-client behavior
- authorization and permission evaluation

The core CloudShell UI shell does not directly know about the Control Plane
domain. It hosts shell integrations such as Resource Manager, settings,
notifications, and other extension-owned surfaces. Resource Manager UI and
similar integrations consume their own public managers and client adapters,
which may be backed by an in-process Control Plane in a combined host or by a
remote Control Plane service. UI integrations should not depend on Control
Plane stores or provider runtime internals.

Resource Manager is the logical facade over the services that manage
resources. The Control Plane side owns inventory, validation, lifecycle,
deployment coordination, orchestration, health, events, and API projection.
Orchestrators are execution adapters inside Resource Manager: they materialize
runtime state for resources and services, but they do not own the resource
graph or replace Resource Manager as the authority.

### Runtime and orchestration boundary

The host application runs CloudShell. The host environment is what CloudShell
manages. The Control Plane owns Resource Manager backend behavior for that
environment: resource graph state, provider registrations, authorization,
commands, lifecycle, health, telemetry, events, persistence, and API
projection.

Resource Manager is the management facade. It accepts resource intent through
resource definitions, persists or projects the accepted graph state, and
coordinates operations against resources. Orchestration is a Resource Manager
execution subsystem, not a separate product surface. It is used when accepted
resource state must be materialized into runtime artifacts such as services,
replica groups, replicas, routing bindings, load-balancer mappings, or
provider-managed runtime resources.

Most resources can remain standalone orchestrated resources: the resource is
the unit Resource Manager starts, stops, inspects, and diagnoses. Container
apps are different because one stable resource represents a managed workload
facade over several runtime artifacts. In that case the container app resource
stays the user-facing API object, while Resource Manager derives internal
orchestrator deployments and service boundaries from accepted app resource
state. Users apply resource intent; orchestrators reconcile runtime artifacts.

The runtime model is therefore layered:

```text
Host application
    hosts CloudShell UI and/or Control Plane

Host environment
    contains resources and runtime artifacts

Control Plane / Resource Manager
    owns resource graph state, commands, API, events, health, telemetry

Resource providers
    validate resource definitions and implement provider-owned behavior

Orchestration
    plans and reconciles runtime materialization when resource state requires it

Runtime providers and hosts
    execute processes, containers, routing, storage, network, or provider APIs
```

The [Resource model](terminology.md#resource-model) is the resource-focused
subset users usually need first. The [Runtime model](terminology.md#runtime-model)
is the fuller management model that includes the Resource model plus
orchestration services, replica groups, replicas, routing bindings, retained
revisions, and environment revisions. Resource Manager may visualize the
Runtime model in the [Environment Map](terminology.md#environment-map), but
that map is a read model over Control Plane and orchestrator state, not another
source of truth.

```mermaid
flowchart TD
    User["User or automation"] --> ResourceManagerUi["Resource Manager UI\nshell extension"]
    ResourceManagerUi --> Managers["Public managers and client adapters"]
    Api["Control Plane API"] --> Managers
    Managers --> Facade["Resource Manager facade"]

    subgraph ControlPlane["Control Plane Resource Manager backend"]
        Facade --> Inventory["Resource inventory\nregistrations, groups, dependencies"]
        Facade --> Lifecycle["Lifecycle procedures\nactions, delete, dependency ordering"]
        Facade --> Deployment["Deployment service\napply attempts and outcomes"]
        Facade --> Health["Health, events, logs\noperational observations"]
        Deployment --> Orchestration["Orchestration service\nruntime materialization"]
        Orchestration --> Revisions["Environment revisions\nmaterialized history"]
        Orchestration --> ReplicaManagement["Replica group reconciliation\nslot runtime state"]
    end

    subgraph Runtime["Runtime providers and orchestrators"]
        Orchestrator["Default or external orchestrator"]
        Providers["Resource providers\nDocker, apps, networking, storage"]
        RuntimeResources["Materialized runtime resources\ncontainers, routes, volumes"]
    end

    Orchestration --> Orchestrator
    Lifecycle --> Providers
    Orchestrator --> Providers
    Providers --> RuntimeResources
    RuntimeResources --> Health
```

## Host topology

CloudShell separates the environment from the host applications and capability
packages that compose it.

### CloudShell environment

A CloudShell environment is the managed local, team-owned, or on-premise
cloud-like environment that users inspect and operate. It is anchored by
Control Plane resource state, installed capability packages, and one or more
UI hosts.

The environment model is deliberately shared between local development and
on-premise hosting. A developer can run the platform locally as a combined
CloudShell UI and Control Plane host while resources are still code-first, then
the same resource model can be persisted into durable Control Plane state and
operated by a standing CloudShell environment. CloudShell is therefore a
hosting platform that doubles as a development tool, not a development
dashboard that must later be replaced by a different operational model.

An on-premise CloudShell environment is a standalone CloudShell cloud
environment, potentially for shared hosting. It owns Control Plane state,
installed capabilities, provider integrations, and runtime placement policy
instead of acting only as a developer workstation process.

### CloudShell host application

A CloudShell host application is the ASP.NET Core application owned by a
product integrator or sample. It chooses deployment shape, configuration,
authentication, persistence, and installed capabilities. A host application can
run CloudShell UI, the Control Plane, or both.

In split deployments, the UI host discovers resources through a remote Control
Plane client instead of declaring resources or hosting providers locally. In
combined local-development deployments, programmatically declared resources
may run from the same host process that hosts CloudShell UI and the Control
Plane, but they are still managed by the same local Control Plane that
coordinates provider behavior, lifecycle actions, and resource projection.

### Capability packages

A CloudShell capability package is an installable environment capability. It
may be vertical, such as Docker support, application resources, configuration
services, or secrets, or cross-cutting, such as networking, identity,
observability, deployment, or policy.

A capability package may contribute:

- Control Plane resource providers and provider-owned services.
- Resource type definitions and programmatic declaration helpers.
- Resource actions, logs, templates, diagnostics, and capabilities.
- Resource Manager UI support such as add/update components, detail views,
  tabs, routes, and UI actions.
- Shell-level UI such as navigation, workspaces, settings pages,
  notifications, named content areas, and operational dashboards.
- SDK clients or helper packages for authored services.

Capability packages are product packaging and environment-capability
boundaries, not resource model entities. A capability package can define
several resource types, and a resource can depend on capabilities from several
packages.

### CloudShell extensions

A CloudShell extension is the in-process registration mechanism a capability
package uses to plug into a host application. Extension registrations are
code-level contracts. Capability packages are the packaging and environment
capability boundary.

Use "capability package" for installable environment capabilities. Use
"extension" for the code-level registration mechanism. Use more specific
terms such as "Control Plane provider integration" or "Resource Manager UI
integration" when the layer matters.

### Workloads

Use "workload" only for runtime application execution concerns, such as
container-image, container-build, ASP.NET Core project, or local executable
configuration. That runtime meaning is distinct from CloudShell capability
packages.

## Extension surfaces

An extension can integrate with CloudShell UI, the Control Plane, or both.
Those are separate layers even when a single capability package installs both
halves into a combined host.

Extensions are guests of the host application. The host loads their
contributions, validates them, adapts them into shell composition or Control
Plane services, and decides which surfaces are active. Even when extensions
run in-process for the current implementation, the architectural dependency
should point toward abstractions and host-provided services, not toward
CloudShell UI implementation details.

This keeps the extension model portable across shell implementations. A
different CloudShell UI can honor the same contribution descriptors and service
contracts while rendering them with its own layout, navigation, settings,
notification, or Resource Manager presenters.

Resource Manager is the canonical large integration. It integrates with:

- CloudShell UI, by contributing pages, navigation, settings sections,
  resource-detail views, UI components, and shell composition adapters.
- The Control Plane, by installing resource-management services, provider
  contracts, lifecycle orchestration, activity recording, API endpoints,
  persistence behavior, and authorization behavior.

The CloudShell UI host sees the Resource Manager side as a shell extension.
The Control Plane service is the backend for that shell extension. A combined
host may load both halves in one ASP.NET Core process, but the architecture is
still "CloudShell UI hosts the Resource Manager shell extension" and "Resource
Manager talks to its backend through public managers/client adapters," not
"CloudShell UI owns Control Plane behavior."

Resource Manager also owns product-specific extension points of its own.
Resource providers extend Resource Manager through resource types, detail
views, actions, templates, provider-backed operational data, and resource
creation/update surfaces. Those provider integrations can span both CloudShell
UI and the Control Plane, but they should still pass through Resource
Manager's public contracts instead of shell internals. The same pattern can
apply to other large integrations that later define their own sub-extension
points.

```mermaid
flowchart LR
    subgraph UIHost["CloudShell UI host"]
        Shell["CloudShell UI shell"]
        ShellContracts["Shell contracts and services\nmenus, pages, sections, settings, notifications"]
        ResourceManagerUi["Resource Manager shell extension"]
        OtherUi["Other CloudShell UI integration"]
    end

    subgraph ControlPlaneHost["Control Plane host"]
        ControlPlane["Control Plane service"]
        ResourceManagerBackend["Resource Manager backend services"]
        ProviderBackend["Resource provider integration"]
    end

    subgraph ResourceManagerContracts["Resource Manager contracts"]
        ResourceManagerExtensionPoints["Resource types, views, actions,\ntemplates, provider data"]
    end

    Shell --> ShellContracts
    ShellContracts --> ResourceManagerUi
    ShellContracts --> OtherUi
    ResourceManagerUi --> ResourceManagerContracts
    ProviderBackend --> ResourceManagerContracts
    ResourceManagerUi -->|"public managers / client adapters"| ControlPlane
    ResourceManagerBackend --> ControlPlane
    ProviderBackend --> ControlPlane
```

## Shared concepts

Some product areas need shared concepts that bridge UI and Control Plane
integrations without merging their implementations.

Resource Manager is the clearest example. Resource Manager concepts such as
resource view IDs, route targets, capability descriptors, contribution
descriptors, settings section IDs, and installation options may be needed by
both UI and backend integrations. Those concepts should live in shared
abstractions that do not depend on Blazor, Fluent UI, Control Plane stores, or
provider runtime implementations.
These abstractions are Resource Manager's integration contracts, not the
CloudShell shell model itself. A resource provider extension can be a Resource
Manager extension while Resource Manager itself remains a CloudShell
extension.

This gives each product area three possible layers:

- shared abstractions for product concepts
- UI integration for CloudShell UI
- Control Plane integration for backend behavior

Capability packages can still provide convenience registration that installs
all relevant layers, but that convenience should not erase the architectural
boundary.

CloudShell UI and extensions may share common abstractions without sharing a
concrete UI implementation. A shell feature such as notifications should expose
an abstraction for producers and consumers, while CloudShell UI can provide an
optional integration package that renders those notifications in the shell.
Extensions should be able to hook into the notification service or another
shell-owned service without referencing the UI components that happen to render
it. The same pattern applies to composition: extension authors should be able
to target menu, page, section, and settings contracts without depending on the
CloudShell UI presenters that render those artifacts.

## Hosting shapes

CloudShell supports several host shapes:

- UI-only host: runs CloudShell UI plus UI integrations such as Resource
  Manager UI; backend-aware integrations call a remote Control Plane through
  their configured adapters.
- Control Plane-only host: runs backend services and APIs without the Blazor
  shell.
- Combined host: runs CloudShell UI and Control Plane together, primarily for
  local development and small self-hosted deployments.

Host registration APIs should make these choices explicit. A combined host can
install both UI and Control Plane integrations, while split hosts install only
the layers they need.

## Design rule

When adding or moving a feature, identify which layer owns it:

- Shell/UI concern: belongs in CloudShell UI or a UI integration package.
- Backend resource-management concern: belongs in the Control Plane or a
  Control Plane integration package.
- Shared product vocabulary: belongs in an abstractions package with no UI or
  backend implementation dependency.
- Convenience host setup: belongs in hosting registration helpers that compose
  the appropriate layers explicitly.
