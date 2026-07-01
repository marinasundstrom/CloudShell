# UI composition library

`CoreShell.Composition` is the reusable layout and content composition
substrate under CoreShell. It stores low-level structure and relationships
between content. It is its own subject, separate from the higher-level
CoreShell toolkit and extension model.

CoreShell uses the library as an implementation foundation for shell-owned
areas, but product integrations should target CoreShell services for menus,
pages, sections, settings, notifications, and other shell-owned areas. The
lower-level composition library remains available to CoreShell implementers,
presenters, and sandbox hosts that need to prove dynamic pages, menus,
sections, route-aware links, layout outlets, and future graph persistence
without adopting CloudShell navigation, Settings, Resource Manager, extension
activation, Fluent UI, or Control Plane concepts.

For the current implementation guide, package split, and sandbox behavior, see
[UI composition](../../ui-composition.md). For the CloudShell product layer
that consumes this library through CoreShell, see
[Shell composition](shell-composition.md).

## Status

Current implementation working document. The library exists experimentally as
`CoreShell.Composition` and `CoreShell.Composition.Blazor`; this
proposal owns the reusable library direction while the shell composition
proposal owns the CoreShell toolkit layer above it.

## Goals

- Keep the reusable composition engine independent from CloudShell Hosting,
  Resource Manager, Fluent UI, Bootstrap component packages, Control Plane
  services, and CloudShell extension activation.
- Model structural UI composition through typed IDs, modules, pages, menus,
  menu groups, menu items, slots or section containers, sections, target
  resolution, route metadata, renderer hints, and validated projections.
- Keep navigation hierarchy separate from content hierarchy. Menus point at
  addressable content or href targets; they do not own page or section
  content.
- Provide plain Blazor integration components that emit ordinary HTML and can
  be styled by any host.
- Support host adapters that project the same graph into Fluent, Bootstrap,
  custom components, dashboards, settings layouts, tabsets, section stacks, or
  other renderers.
- Preserve a serializable descriptor path for future persistence of durable
  graph metadata such as IDs, ownership, ordering, target relationships,
  route/deep-link metadata, and renderer hints.

## Non-goals

- Do not make the library the CoreShell UI extension API. Shell integrations
  should use CoreShell abstractions and services that project into this graph.
- Do not encode CloudShell product areas such as main navigation, Settings,
  Resource Manager, notifications, or provider workspaces as generic library
  concepts.
- Do not depend on Fluent UI or another visual component framework from the
  reusable Blazor package.
- Do not make the library responsible for CloudShell extension activation,
  capability-package trust, product policy, authorization decisions,
  localization boundaries, or Control Plane behavior.
- Do not persist executable component types as arbitrary data. Persistence
  should target graph metadata and leave component activation under host-owned
  code.

## Ownership Boundary

The UI composition library owns structure. It should answer questions such as:

- What artifacts exist in the composition graph?
- Which module contributed each artifact?
- Which artifacts are addressable by typed IDs?
- Which menu items target which content or hrefs?
- Which sections belong to which section containers?
- Which routes or child address values resolve to which pages or sections?
- Which metadata and renderer hints are available to host presenters?

The library should not answer CloudShell product questions such as:

- Which areas belong in the CloudShell main navigation?
- Which settings categories are stable CloudShell extension targets?
- Whether a settings hierarchy is rendered as tabs, a sidebar, or another
  layout.
- Which extension packages are active, trusted, disabled, or unloadable.
- Which CloudShell permissions hide, disable, or block a contribution.
- Which Fluent UI component should present a shell concept.

Those decisions belong to CoreShell or to another host that consumes the
library.

## Current Library Shape

The current implementation is intentionally small:

- `CoreShell.Composition` contains typed IDs, module registration,
  registry assembly, descriptors, validation, target-to-link resolution, page,
  menu, section-outlet, and section projections.
- `CoreShell.Composition.Blazor` contains plain Blazor components for
  composition hosts, menus, links, title outlets, page layouts, section
  outlets, section navigation, and tabbed section layouts.
- `samples/CompositionSandbox` proves the same graph outside CloudShell
  Hosting with ordinary Blazor routing and host-owned styling.
- CloudShell Hosting currently consumes the graph through adapters and
  presenters, but those adapters should stay above the library boundary.

## Relationship to CoreShell

CoreShell should be a toolkit and adapter layer over this library, not part of
the library itself. Shell-owned abstractions can define durable shell areas
such as:

- main navigation
- common Settings
- notification center
- dashboards
- provider workspaces
- Resource Manager shell integration
- documented extension areas

Those abstractions should project into UI composition artifacts when the graph
is assembled. That lets shell and CloudShell extensions use focused CoreShell
services without directly registering composition pages, menus, and sections,
while still letting the shell reuse the generic graph, route resolution,
renderer hints, and future persistence path.

## Open questions

- Which generic metadata belongs in first-class descriptor properties, and
  which belongs in a namespaced attribute bag?
- What is the exact descriptor, runtime instance, and renderer projection
  split?
- Which typed ID factories are needed for slots, section containers, sub-pages,
  command targets, and future persisted artifacts?
- How should route templates, route values, fragments, query state, and child
  address values be represented without leaking host-specific URL policy?
- What is the minimum persistence contract that is useful without making
  executable UI database-owned?
