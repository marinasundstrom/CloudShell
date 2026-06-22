# Resource Manager project structure

## Status

Proposed.

This proposal describes the desired logical and physical structure for
Resource Manager assemblies, extension contracts, and provider integrations.
It is not an immediate MVP refactor. The current priority remains stabilizing
the local-development Resource Manager experience, then using that experience
to guide the eventual split.

## Problem

Resource Manager is currently implemented mostly inside `CloudShell.Hosting`.
That made sense while the shell and Resource Manager evolved together, but it
no longer reflects the product structure:

- CloudShell Hosting is the shell UI host.
- Resource Manager is a built-in shell product area.
- The Control Plane owns resource inventory, lifecycle, activity, logs,
  providers, persistence, and API behavior.
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
- Separate Resource Manager UI integration contracts from Control Plane
  provider contracts.
- Keep CloudShell UI deployable independently from the Control Plane.
- Let provider packages contribute Resource Manager views without depending on
  Control Plane internals.
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

## Desired logical layers

### CloudShell shell

The shell owns common application chrome and shell-level services:

- main layout and top bar
- main navigation presenter
- common Settings surface
- notification UI and future notification persistence adapters
- shell-owned composition adapters and Fluent UI presenters
- generic shell pages that are not Resource Manager-specific

The shell should depend on Resource Manager UI only when the host wants to
install the Resource Manager product area.

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

### Resource Manager Control Plane

Resource Manager Control Plane owns resource state and resource operations:

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

## Candidate physical assemblies

The exact names can still change, but the desired split is:

| Project | Responsibility |
| --- | --- |
| `CloudShell.Hosting` | CloudShell shell host, shell chrome, shell services, shell composition adapters, and non-Resource-Manager pages. |
| `CloudShell.ResourceManager.UI.Abstractions` | Public Resource Manager UI contracts: resource view contribution descriptors, create/update view contracts, route/link targets, Resource Manager composition IDs, UI capability descriptors, and extension registration builders. |
| `CloudShell.ResourceManager.UI` | Built-in Resource Manager UI implementation: pages, Resource Manager Fluent presenters, generated resource views, Resource Manager settings sections, and adapters from Resource Manager UI contracts into shell composition. |
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
