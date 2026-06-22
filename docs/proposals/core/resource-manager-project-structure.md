# Resource Manager project structure

## Status

Proposed.

This proposal applies the general CloudShell application-layer model from
[CloudShell architecture](../../architecture.md) to Resource Manager. It
describes the desired logical and physical structure for Resource Manager
assemblies, shared concepts, extension contracts, and provider integrations.
It is not an immediate MVP refactor. The current priority remains stabilizing
the local-development Resource Manager experience, then using that experience
to guide the eventual split.

## Problem

Resource Manager is currently implemented mostly inside `CloudShell.Hosting`.
That made sense while the shell and Resource Manager evolved together, but it
no longer reflects the product structure:

- CloudShell UI is an extensible Blazor application. It may act as a shell for
  integrations and may run inside a host application by itself or together
  with the Control Plane.
- The common shell infrastructure is expected to become CoreShell: a
  product-neutral shell layer with extension contracts, shell services,
  CMS-like composition infrastructure, and presenter abstractions.
- CloudShell is the product host that uses CoreShell, the default Fluent UI
  presenters, and predefined integrations such as Resource Manager.
- The Control Plane is a backend application. It may be hosted with
  CloudShell UI for local development, or independently in split and
  on-premise deployments.
- Resource Manager is an extension/product area that integrates with both
  CloudShell UI and the Control Plane.
- The Control Plane owns resource inventory, lifecycle, activity, logs,
  providers, persistence, and API behavior.
- Resource Manager owns common concepts that must be shared across its UI and
  Control Plane integrations.
- Provider packages can contribute both Control Plane behavior and Resource
  Manager UI, but those are separate extension surfaces.

Keeping Resource Manager pages, Resource Manager UI contracts, shell chrome,
and Control Plane/provider registration close together makes it harder to tell
which layer owns a change. It also makes future split hosting, package
composition, UI-only hosts, and Resource Manager-specific extension contracts
less obvious.

## Goals

- Make Resource Manager a product area that plugs into CloudShell UI, not an
  accidental part of the shell host project.
- Define Resource Manager common concepts in a layer that can be shared by its
  UI and Control Plane integrations without forcing either host surface to
  depend on the other's implementation.
- Separate Resource Manager UI integration contracts from Control Plane
  provider contracts.
- Keep CloudShell UI deployable independently from the Control Plane.
- Let provider packages contribute Resource Manager views without depending on
  Control Plane internals.
- Let UI extension packages contribute Resource Manager views without
  depending on the concrete CloudShell UI host package or shell implementation
  project.
- Keep CloudShell UI isolated from extension implementations by routing
  extension participation through Resource Manager UI abstractions,
  shell-owned services, and adapter layers.
- Let provider packages contribute Control Plane resource behavior without
  depending on Blazor, Fluent UI, or shell rendering.
- Preserve the option for convenience packages that install both halves into a
  combined host.
- Keep public contracts smaller than internal implementation projects.
- Avoid exposing today's internal Resource Manager presenters as durable
  extension contracts before the shell composition and Resource Manager
  adapters are ready.

## Non-goals

- Do not move projects only to satisfy naming preferences.
- Do not block MVP stabilization on the final assembly split.
- Do not make Resource Manager the generic CloudShell shell model.
- Do not require every provider to split into several packages immediately.
- Do not move Control Plane behavior into UI projects.
- Do not make UI extensions depend on Control Plane stores such as
  `IResourceManagerStore`.
- Do not make UI extensions reference `CloudShell.Hosting` only to access
  Resource Manager extension contracts or stable shared UI components.

## Desired logical layers

### CloudShell UI

CloudShell UI is the product host application. It may run by itself, or
alongside the Control Plane in a combined local development host. It assembles
CoreShell, the default Fluent UI presenter layer, and predefined integrations
such as Resource Manager.

### CoreShell

CoreShell is the intended name for the common UI infrastructure until a better
name exists. It is the product-neutral shell layer that owns common application
chrome, CMS-like composition infrastructure, extensibility contracts, and
shell-level services:

- main layout and top bar
- main navigation presenter
- common Settings surface
- notification UI and future notification persistence adapters
- shell-owned composition adapters
- generic shell pages that are not Resource Manager-specific

CoreShell should expose extension points and service contracts through a
framework-neutral layer, tentatively `CoreShell.Extensibility`. Shell
presenters that depend on Fluent UI should live in a separate default presenter
layer, tentatively `CoreShell.FluentUI`. CloudShell uses that Fluent UI layer
as the default look and feel, but another host should be able to implement its
own presenters over the same CoreShell contracts.

`CoreShell.FluentUI` should expose components and integration helpers that a
host web app can directly use: navigation menu presenters, shell layout
presenters, settings presenters, section/tab layout presenters, notification
surfaces, and other Fluent UI renderers over CoreShell contracts. It is a
default presenter package, not the source of the CMS-like model itself.

CloudShell UI should depend on Resource Manager UI only when the product host
wants to install Resource Manager as one of its predefined product areas.

### Control Plane

The Control Plane is the backend application. It can run with CloudShell UI or
independently. It owns backend services for resource inventory, provider
coordination, lifecycle operations, logs, templates, API projection,
persistence, validation, and authorization. CloudShell UI talks to it through
public domain managers and remote adapters instead of relying on in-process
stores.

### Resource Manager common concepts

Resource Manager needs a common concepts layer that is shared across its UI
and Control Plane integrations. This layer should define stable names, IDs,
capabilities, route/link targets, contribution descriptors, and host
installation concepts that both sides need to agree on.

This common layer should not contain Blazor components, Fluent UI presenters,
Control Plane stores, provider execution, or persistence code. It exists so a
Resource Manager UI integration and a Resource Manager Control Plane
integration can describe the same resource-management concepts without sharing
implementation details.

### Resource Manager UI

Resource Manager UI owns the user-facing resource management experience:

- resource list and resource detail pages
- resource detail view/tab model and adapters
- generated resource overview, endpoints, DNS, identity, access control,
  health, monitoring, activity, logs, traces, metrics, storage, and
  configuration surfaces
- Resource Manager-specific Settings sections
- Resource Manager-specific UI components and layout helpers
- Resource Manager link helpers, route targets, and composition IDs
- UI extension contracts that provider UI packages use to contribute create,
  update, detail, action, and diagnostic views

Resource Manager UI should consume public domain managers such as
`IResourceManager`, `ILogManager`, `ITraceManager`, and future client
abstractions. It should not depend directly on Control Plane stores or
provider runtime internals.

Provider and extension UI packages should depend on Resource Manager UI
abstractions and stable shared component libraries, not on the concrete
CloudShell UI host assembly. `CloudShell.Hosting` should remain the shell host
and built-in presenter implementation, while extension-facing contracts live
in abstractions packages and reusable UI building blocks live in a shared
component package.

The intended direction is that CloudShell UI consumes extension contributions;
extensions do not consume CloudShell UI internals. The UI host should expose
integration points, services, and adapters for contribution registration and
rendering. Resource Manager UI abstractions should describe what can be
contributed, while the concrete shell decides how those contributions are
validated, composed, authorized, localized, and rendered.

Those contracts should not require the current Fluent UI-based CloudShell host.
Another CloudShell UI implementation should be able to consume the same
Resource Manager UI contribution descriptors and public services, then render
them with a different component stack or presenter package.

CloudShell UI may also provide optional integration packages that bind shared
abstractions to the concrete shell experience. For example, a future
notification abstraction should let extensions publish or subscribe to
notifications without knowing which UI component renders them; CloudShell UI
can then provide the notification presenter package that hooks the abstraction
into toasts, off-canvas history, or other shell-specific surfaces.

### Resource Manager Control Plane integration

The Resource Manager Control Plane integration owns or installs backend
resource-management behavior:

- resource inventory and registration
- groups, dependencies, relationships, and resource source metadata
- lifecycle procedures and provider orchestration
- activity/event recording
- provider contracts and provider execution
- persistence stores and reconciliation
- API projection and remote client behavior
- authorization and resource permission evaluation

This layer should not depend on Blazor, Fluent UI, or Resource Manager UI
components.

### Provider integrations

A provider may have two independent contribution halves:

- a Control Plane provider integration, which contributes resource types,
  resource projection, lifecycle, validation, diagnostics, capabilities, and
  provider-owned runtime behavior
- a Resource Manager UI integration, which contributes create/update/detail
  views, generated view enrichments, icons, actions, and provider-specific
  resource pages

The two halves can remain in one package while the model is still moving, but
the contracts should make the boundary clear. Longer term, a provider can ship
separate packages such as a runtime/provider package and a Resource Manager UI
package, plus an optional convenience package that installs both.

Both halves can depend on Resource Manager common concepts. The UI half should
depend on Resource Manager UI abstractions and stable shared components; the
Control Plane half should depend on Control Plane/provider abstractions.
Neither half should require the other to be installed in the same process, and
the UI half should not reference the full CloudShell UI host package just to
register or render Resource Manager contributions.

## Candidate physical assemblies

The exact names can still change, but the desired split is:

| Project | Responsibility |
| --- | --- |
| `CloudShell.CoreShell` | Common shell infrastructure: shell services, composition integration, shell-owned standard areas, extension point registration, and product-neutral shell logic. |
| `CloudShell.CoreShell.Extensibility` | Public shell extension contracts and service abstractions for menus, pages, sections, settings, notifications, and other shell-owned areas. |
| `CloudShell.CoreShell.FluentUI` | Default Fluent UI presenters and host-usable components for CoreShell contracts, including navigation menu integrations, settings presenters, notification surfaces, shell layouts, section/tab presenters, and common shell visual language. |
| `CloudShell.Hosting` | CloudShell product host that assembles CoreShell, the default Fluent UI presenters, predefined integrations, authentication, localization, and host-specific wiring. |
| `CloudShell.Components` | Stable shared UI components that extension UI packages can consume without referencing the concrete CloudShell UI host. Components here should avoid Hosting-only services and Resource Manager implementation dependencies. |
| `CloudShell.ResourceManager.Abstractions` | Shared Resource Manager concepts used across UI and Control Plane integrations: stable IDs, names, contribution descriptors, capability descriptors, route/link target concepts, installation concepts, and extension registration primitives that are not tied to Blazor or Control Plane stores. |
| `CloudShell.ResourceManager.UI.Abstractions` | Public Resource Manager UI contracts: resource view contribution descriptors, create/update view contracts, Resource Manager composition IDs, UI capability descriptors, and UI registration builders. This should depend on shared Resource Manager abstractions, not Control Plane implementation. |
| `CloudShell.ResourceManager.UI` | Built-in Resource Manager shell extension: pages, Resource Manager Fluent presenters or presenter adapters, generated resource views, Resource Manager settings sections, and adapters from Resource Manager UI contracts into CoreShell composition. |
| `CloudShell.ResourceManager.Hosting.Abstractions` | Host-level Resource Manager installation contracts shared by combined hosts, split UI hosts, and split Control Plane hosts. This should describe how a host opts into Resource Manager capabilities without forcing both UI and Control Plane into one process. |
| `CloudShell.ResourceManager.Hosting` | Host registration helpers that install Resource Manager services into the appropriate host shape. In a UI host, this installs Resource Manager UI and remote-client dependencies. In a Control Plane host, this installs resource manager services, API endpoints, stores, providers, and orchestration. In a combined host, it can install both through explicit options. |
| `CloudShell.ControlPlane` | Control Plane implementation, including Resource Manager domain services until or unless a later split creates a dedicated Control Plane Resource Manager implementation assembly. |
| `CloudShell.ControlPlane.Client` | Remote Resource Manager and Control Plane client adapters used by UI-only hosts and external consumers. |

One possible later refinement is to split the Control Plane Resource Manager
implementation further, for example `CloudShell.ResourceManager.ControlPlane`
and `CloudShell.ResourceManager.ControlPlane.Abstractions`. That should wait
until the Control Plane boundary needs it. The immediate structural problem is
the UI and shell boundary, not an urgent Control Plane assembly split.

## Provider package shape

Provider packages should be able to choose one of three shapes:

| Shape | Example | Use |
| --- | --- | --- |
| Combined provider package | `CloudShell.Providers.Applications` | Early development or small built-in capability where UI and Control Plane move together. |
| Split provider and UI packages | `CloudShell.Providers.Applications` and `CloudShell.Providers.Applications.ResourceManager.UI` | Clear runtime/UI separation for split hosting and optional UI installation. |
| Convenience package | `CloudShell.Providers.Applications.Hosting` | Installs the provider and Resource Manager UI integration together for combined local-development hosts. |

Even when one assembly contains both halves, code should be organized so the
Control Plane provider does not need Blazor pages and the UI contribution does
not need provider internals beyond public client/domain abstractions.

## Migration approach

1. Keep current MVP stabilization inside existing projects.
2. Move Resource Manager-specific component and page organization toward clear
   feature folders so future project moves are mostly mechanical.
3. Define Resource Manager UI contracts on top of current tab/view/create/update
   behavior without exposing internal presenters.
4. Move Resource Manager shared UI components and pages from
   `CloudShell.Hosting` into `CloudShell.ResourceManager.UI`.
5. Leave `CloudShell.Hosting` with shell chrome and composition integration
   points, then install Resource Manager UI as a shell product area.
6. Split provider UI contributions from provider runtime services when split
   hosting or package composition requires it.
7. Add compatibility registration helpers so existing combined-host samples can
   opt into Resource Manager with one explicit call while internally installing
   the correct UI and Control Plane halves.

## Open questions

- Should `CloudShell.ResourceManager.Hosting` install both UI and Control Plane
  halves, or should the final naming use clearer `*.UI.Hosting` and
  `*.ControlPlane.Hosting` packages?
- Should Resource Manager Control Plane contracts stay in
  `CloudShell.Abstractions.ResourceManager`, or should they move into a
  dedicated Resource Manager abstractions package once the split is real?
- How much of today’s resource tab/view contribution model should survive as a
  Resource Manager-specific abstraction after it adapts to shell composition?
- What is the smallest compatibility layer that lets existing provider packages
  keep working while the Resource Manager UI moves out of `CloudShell.Hosting`?
- Should provider UI packages be optional in headless Control Plane hosts, and
  how should missing UI integrations be reported in Resource Manager?

## Relationship to shell composition

This proposal is related to, but separate from, shell composition. Shell
composition defines how CloudShell exposes layout and content extension points.
This proposal defines where Resource Manager belongs physically and logically
as one shell product area.

Resource Manager should eventually consume shell composition through an
adapter. Its resource-detail tabs, settings sections, and generated resource
views can be projected into shell composition nodes, but Resource Manager owns
the resource-specific vocabulary and validation. The generic composition
library should not become a Resource Manager API, and Resource Manager should
not continue to live inside the generic shell host simply because it is the
largest current UI surface.
