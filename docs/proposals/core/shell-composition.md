# Shell composition

CloudShell UI should become an independently useful extensible shell, not only
the host for Resource Manager. Resource Manager remains the most important
first consumer, but the shell composition model should be general enough for
provider workspaces, settings, notifications, dashboards, and future product
areas.

The shell composition surface should be a layout/content engine, not a tab
engine or a navigation-menu API. Extensions contribute stable layout nodes to a
host-owned graph. Navigation hierarchy is separate from content hierarchy.
Menu nodes describe how users navigate; content nodes describe pages,
sub-pages, slots, section containers, and sections. A menu item can target a
content node by ID, but it does not own the content hierarchy. Renderers then
project the graph as menu navigation, pages, sections, tabs, settings layouts,
dashboards, or custom extension-owned layouts. Resource Manager tabs are one
adapter over this model, not the generic model itself.

## Status

Proposed. This is post-MVP platform direction. MVP work should keep Resource
Manager and supported local-development samples stable while avoiding
short-term UI decisions that would prevent this model.

## Goals

- Let extensions contribute pages, sub-pages, slots, section containers,
  sections, menu entries, settings surfaces, notifications, and hosted
  workspaces through stable layout contracts.
- Keep the CloudShell UI deployable without the Control Plane. Resource
  Manager is an extension over the shell, not the definition of the shell.
- Let Resource Manager fully exploit the same shell primitives for resource
  pages, grouped resource menus, tabbed resource details, common settings,
  notifications, and extension areas.
- Keep host-owned validation for duplicate IDs, conflicting content route
  metadata, missing dependencies, permission requirements, and invalid
  ordering.
- Keep the contribution model independent from a specific visual metaphor.
  Tabs, side navigation, accordions, section stacks, dashboards, and custom
  renderers should all consume the same graph-shaped layout model where
  practical.
- Let links, selected state, and deep-link generation be rendered from content
  IDs and target relationships instead of each extension hard-coding
  URL/query-string details.
- Preserve split hosting: UI contributions run in the UI host and call public
  domain managers or remote adapters, while Control Plane providers remain in
  the Control Plane host.
- Start with programmatic composition through capability packages and grow
  toward permissioned UI configuration later.

## Non-goals

- Do not make extensions own arbitrary shell layout markup or bypass host
  validation.
- Do not make Resource Manager-specific concepts the generic shell model.
- Do not make the generic composition API depend on tabs, menus, settings, or
  Resource Manager terminology.
- Do not introduce a persisted layout editor before the programmatic contracts
  are stable.
- Do not move resource lifecycle, provider configuration, authorization, or
  Control Plane behavior into UI contributions.

## Layout graph model

The target composition model is a host-owned graph of layout nodes. Each node
has a stable hierarchical ID, a kind, owner metadata, ordering, permissions,
localization metadata, optional component host metadata, and relationships to
other nodes. IDs encode hierarchy within the relevant tree, but navigation and
content remain separate trees:

- navigation hierarchy: menus, menu item groups, menu items, and sub-menu items
- content hierarchy: pages, sub-pages, slots, section containers, and sections

Content nodes are addressable by ID. A page, sub-page, slot, section
container, or section may be targeted by another layout node, a generated link,
a selection state, a diagnostic, or a future deep link even when it is not
itself a routable page. The layout/content engine can look up a content ID,
resolve the nearest routable page, and construct a page link, selected-state
link, fragment, focus target, or renderer-local anchor for the requested
content node.

Menus are hierarchical presenters of navigation. Menu items and sub-menu items
can point to the unique hierarchical ID of any addressable content node: a
page, sub-page, slot, section container, or section. The menu renderer does not
need to know whether the target is implemented as a Razor page, a tab, an
accordion section, a dashboard region, or a custom layout. It asks the
layout/content engine to resolve the target ID into a navigable link.

Routing remains a separate concern from content registration. Razor components
can still declare routes through the normal `@page` directive. The
layout/content engine registers optional route metadata against a page content
ID so it can resolve links and deep links; it does not replace Razor routing or
make every content node routable.

Relationships describe cross-tree links and non-hierarchical targets such as a
menu item targeting a page, a page content node binding to a route, component
hosting, and renderer-specific selection; they do not require a specific
visual treatment.

Hierarchical IDs should be treated as stable product addresses, not display
labels. For example:

```text
menu.workspace
menu-item-group.workspace.observability
menu-item.workspace.resources
sub-menu-item.workspace.observability.traces
page.resource-manager.resources
page.resource-manager.resource.details
sub-page.resource-manager.resource.details.overview
page.settings.identity
slot.settings.identity.main
section-container.settings.identity.providers
section.settings.identity.providers
slot.resource-manager.resource.overview.main
section-container.resource-manager.resource.overview.summary
section.resource-manager.resource.overview.summary
```

The exact naming constants should live in public abstractions when extension
authors need to target them. Display text, localization, iconography, and
ordering remain metadata on the node rather than inferred from the ID.

The graph should make these relationships explicit:

- a page belongs to a content hierarchy and may be bound to a Razor route
- a sub-page belongs to a page
- a slot belongs to a page, sub-page, or renderer-owned layout
- a section container belongs to a slot
- a section belongs to a section container
- a menu item or sub-menu item points to an addressable content node, command,
  route, or external link by ID
- a section, action, diagnostic, or generated link can point to an addressable
  content node by ID
- a settings section belongs to the standard settings page or to a settings
  category
- a Resource Manager tab adapts to a sub-page or section-container target while
  preserving resource-specific context
- a slot or section container declares the context and ordering rules for
  contributed content

The host validates the graph before rendering. It resolves IDs, detects
duplicates, validates required hierarchical parents, target links, and slot
targets, applies permission and visibility rules, and produces
renderer-specific projections. A renderer may choose tabs, a local navigation
menu, a section stack, a dashboard grid, or a custom layout, but the underlying
navigation hierarchy, content hierarchy, and cross-tree relationships stay
ID-based.

Links should be created from layout targets instead of raw routes whenever the
target is a registered layout node. A menu item, tab, button, or section link
can point to a content ID, and the renderer or content engine chooses the
registered route, query parameters, fragment, selected state, and
disabled/not-found behavior. Adapters can keep domain-specific URL
compatibility, such as Resource Manager's existing `tab=<group>:<view>` links,
while still projecting from the generic graph.

For example, a menu item targeting
`section.resource-manager.resource.overview.summary` may resolve to the
Resource Manager details route with the Overview sub-page selected and a
section focus/fragment applied. A menu item targeting `page.settings.identity`
may resolve directly to the Settings route with the Identity page selected.

## Contribution model

The target shell model should include these contribution categories:

| Category | Purpose |
| --- | --- |
| Layout node | The base contribution unit with a stable ID, owner, kind, order, permissions, visibility rules, and relationships to other nodes. |
| Menu | A stable top-level navigation presenter encoded by ID, title, icon, order, permissions, and optional visibility rules. |
| Menu item group | A hierarchical navigation grouping inside a menu, encoded by ID, for related menu items or sub-menu items. |
| Menu item | A navigation presenter targeting a registered content node, route, external link, or command. |
| Sub-menu item | A nested navigation presenter encoded by ID for grouped experiences such as Observability, Resource Manager, settings, or provider workspaces. |
| Page | A shell-hosted content surface contributed by an extension and addressable by content ID. It may be bound to a Razor route, but routing is not the page's hierarchy. |
| Sub-page | A nested page addressable by hierarchical ID and rendered by the parent page's chosen renderer. |
| Slot | A named placement point declared by a page, sub-page, or renderer-owned layout. A slot defines expected context and accepted content kinds. |
| Section container | A named container for ordered sections, usually placed in a slot. It declares expected context, ordering rules, and renderer behavior. Custom extension areas are section containers or slots with documented context and targets. |
| Section | An ordered piece of content contributed to a section container. |
| Settings page | A standardized settings contribution rendered inside the common settings surface through the same page/sub-page/slot/section-container/section model. |
| Notification source | A provider of durable or transient shell notifications. |

The host should own menu rendering, content-link resolution, ordering,
validation, accessibility, localization boundaries, and permission-aware
visibility. Razor and Blazor still own route matching. Extensions own their
components, labels, icons, capability metadata, content IDs, optional route
metadata, and calls to public domain managers.

## Reusable composition engine investigation

CloudShell already has two similar UI composition paths:

- Shell-hosted views use `CustomShellViewContribution` and
  `CustomShellViewMenuItemContribution`. They provide one hosted route, an
  ordered local menu, selected-item routing through the `item` query string,
  and dynamic component rendering.
- Resource Manager uses `ResourceTabContribution`,
  `ResourcePredefinedViewSectionContribution`, `ResourceViewId`, generated
  fallback tabs, predefined view validation, resource-tab grouping, selected
  tab routing through the `tab` query string, and dynamic component rendering
  with resource context parameters.

This means the reusable shape exists, but it is currently split between shell
and Resource Manager concepts. The shared part is not "resource tabs"; it is a
general layout graph and rendering engine:

- stable hierarchical layout IDs for navigation nodes and content nodes while
  keeping navigation hierarchy separate from content hierarchy
- cross-tree target relationships from navigation nodes to pages, sub-pages,
  commands, routes, or external links
- optional route metadata bound to a page content ID
- parent/child, target, and slot relationships between layout nodes
- ordered menu item groups
- ordered menu items, sub-menu items, pages, and sub-pages
- selected item state in the URL
- dynamic component hosting
- optional component parameter/context injection
- slots and section containers with ordered sections
- renderer-neutral links to layout IDs
- host validation for duplicate IDs, unknown targets, invalid replacement, and
  unsupported slots or section containers

Resource Manager should become an adapter over that engine rather than the
source model for it. `ResourceTabContribution` and predefined resource views
should remain resource-specific contracts because they carry resource type,
resource visibility, generated fallback, predefined concern, apply-button, and
resource context semantics. The reusable engine should provide the layout and
composition primitives underneath those contracts.

## Proposed layering

The future shell composition stack should be layered like this:

| Layer | Owns | Examples |
| --- | --- | --- |
| Layout graph primitives | Generic hierarchical IDs, node kinds, relationships, navigation nodes, pages, sub-pages, slots, section containers, sections, ordering, permissions, routing metadata, selection state, and component host metadata. | `ShellLayoutNodeContribution`, `ShellPageContribution`, `ShellSlotContribution`, `ShellSectionContainerContribution`, `ShellSectionContribution`, `ShellMenuProjectionContribution` |
| Layout/content registry and validation | Registration, ID resolution, duplicate detection, missing-target detection, route metadata conflicts, permission rules, visibility rules, and renderer-ready projections. | `IShellLayoutRegistry`, `ShellLayoutGraph`, startup validation diagnostics |
| Shell composition renderer | Common layout, grouped local navigation, content-ID link resolution, URL selection, dynamic component rendering, empty/not-found states, permission-aware visibility, and slot/section rendering. | Hosted workspace layout, settings layout, Resource Manager detail layout, dashboard layout |
| Product adapters | Domain-specific contribution APIs that project into shell composition primitives while preserving their own vocabulary and validation. | Resource Manager tabs, settings pages, notification center pages, provider workspaces |
| Domain services | Control Plane or provider behavior behind the UI. | `IResourceManager`, `ITraceManager`, identity provider hooks, provider settings contracts |

This lets Resource Manager reuse the same layout engine as other shell pages
without leaking resource-specific vocabulary into general shell APIs.

## Resource Manager extraction path

The practical path should be incremental:

1. Introduce the layout graph vocabulary and registry for the separate
   navigation and content hierarchies: menus, menu item groups, menu items,
   sub-menu items, pages, sub-pages, slots, section containers, and sections,
   without replacing existing Resource Manager tabs or shell view menu items
   yet.
2. Dogfood the graph with a standard Settings page made from shell pages,
   sub-pages, slots, section containers, and sections. The initial settings
   experience can render as tabs, but the contract should remain layout-node
   based rather than tab based.
3. Extract a generic ordered-section renderer from
   `GeneratedResourceViewLayout` and `ResourcePredefinedViewSections`, keeping
   the current Resource Manager section contracts as adapters.
4. Extract a generic grouped local-navigation renderer from the Resource
   Manager tab grouping and the shell-hosted view menu item model. Treat tab
   rendering as one renderer over child layout nodes.
5. Let `CustomShellViewContribution`, the new Settings page, and Resource
   Manager detail pages render through the same hosted-page/layout graph
   infrastructure where their needs overlap.
6. Add generic slot and section-container contributions and map
   `ResourcePredefinedViewSectionContribution` into section contributions for
   predefined resource views.
7. Add notification-center and dashboard adapters over the same primitives.
8. Only after the generic renderer is proven, consider renaming or replacing
   `CustomShellView` APIs with clearer shell composition names.

During the extraction, Resource Manager behavior should not regress:

- resource tab IDs remain `ResourceViewId`
- existing `tab=<group>:<view>` links continue to work
- predefined resource view visibility remains resource-shape and capability
  driven
- generated fallback tabs remain Resource Manager-owned
- apply-button behavior remains tied to resource configuration context
- provider-owned predefined view sections continue to receive resource
  context parameters

## Initial API sketch

The generic contracts should be small and preview-marked at first. A possible
shape:

```csharp
builder.Layout.AddPage<AcmeSettings>(
    id: "page.settings.acme",
    title: "Acme",
    parentId: "page.settings",
    route: "/settings/acme",
    order: 10);

builder.Layout.AddSlot(
    id: "slot.settings.acme.main",
    pageId: "page.settings.acme",
    order: 10);

builder.Layout.AddSectionContainer(
    id: "section-container.settings.acme.connection",
    slotId: "slot.settings.acme.main",
    title: "Connection",
    order: 10);

builder.Layout.AddSection<AcmeSettingsConnection>(
    containerId: "section-container.settings.acme.connection",
    id: "section.settings.acme.connection",
    title: "Connection",
    order: 10);

builder.Layout.AddMenuItem(
    id: "menu-item.settings.acme",
    groupId: "menu-item-group.settings.extensions",
    targetContentId: "page.settings.acme",
    order: 40);
```

The examples above are intentionally about IDs and relationships. A settings
renderer may show the page as a tab, a side-navigation item, or a section in a
larger settings page without changing the contribution contract.

Navigation can also target deeper content without becoming its parent:

```csharp
builder.Layout.AddMenuItem(
    id: "menu-item.settings.acme.connection",
    groupId: "menu-item-group.settings.extensions",
    targetContentId: "section.settings.acme.connection",
    order: 41);
```

Resource Manager can then keep its existing API while internally adapting to
the shell primitives:

```csharp
builder.AddResourcePredefinedViewSection<AcmeEndpointPolicy>(
    "acme.gateway",
    ResourcePredefinedViewIds.Endpoints,
    "acme.endpoint-policy",
    "Endpoint policy",
    50);
```

The second call remains the right public API for resource-provider authors
because it expresses Resource Manager intent. The first set is for generic
shell content nodes and navigation presenters.

## Settings

CloudShell should provide a standardized settings page with extension-owned
sections or pages. This gives providers a home for shell or capability
configuration without each provider inventing its own settings route.

Settings contributions must declare whether their state is UI-local,
Control Plane-backed, provider-backed, or external-service-backed. The shell
should not persist provider configuration directly unless the provider exposes
a domain-shaped settings contract.

The standard Settings page is the first good dogfooding target for the layout
graph. It should be built from generic pages, sub-pages, section containers,
slots, and sections, even if the initial renderer presents sub-pages as tabs.
Extension authors should not be forced to think in tabs; they should
contribute a settings page, sub-page, slot, section container, or section with
stable IDs, state-boundary metadata, permissions, and component metadata. The
settings renderer decides how the layout is presented.

## Notifications

CloudShell should provide a notification system with two presentation modes:

- Toasts for transient, immediate feedback.
- An off-canvas notification center for durable notification history,
  unread state, and operator review.

Notification records should identify their source extension, severity,
timestamp, optional resource or route target, and optional action affordances.
Resource lifecycle events, permission denials, provider diagnostics, and
long-running operation results can feed this system without every feature
building its own notification UI.

## Slots and extension areas

Slots and section containers let extensions add content to existing pages
without replacing the page. Resource Manager already has a version of this
pattern with predefined resource view sections. The broader shell should
generalize it for overview pages, settings pages, notification details, and
provider workspaces. Navigation chrome should remain a navigation presenter
that can target content IDs rather than a content hierarchy itself.

Extension areas should be explicit and documented as slots or section
containers. They are not arbitrary DOM injection points; each slot or container
should define its expected context, ordering rules, permission behavior, and
lifecycle.

Slots and section containers are nodes in the layout graph, not markup holes. A
page or renderer declares slot IDs and context types. Section containers are
placed into slots, and extensions contribute sections to those containers by
ID. Custom extension areas use the same slot and section-container concepts
with documented targets and context. The layout registry validates target
slots and containers, and the renderer controls how sections are ordered,
collapsed, tabbed, stacked, or otherwise presented.

## Relationship to Resource Manager

Resource Manager should be treated as a large shell extension that uses the
same contribution model as other shell areas. It may contribute menu nodes,
resource pages, settings entries, notifications, slots, and section
containers. Resource
providers can then extend Resource Manager through resource-specific contracts
while still relying on generic shell primitives for navigation, layout,
settings, and notifications.

For the local-development MVP, Resource Manager remains the primary proof:
application topology, settings, secrets, identity, storage, networking,
exposure, telemetry, monitoring, and diagnostics must be understandable there
before broad new shell surfaces become release blockers.

## Open questions

- Which shell composition settings are environment-global, tenant-scoped,
  role-scoped, or user-scoped?
- How should an extension declare permission requirements for shell groups,
  pages, settings sections, and notification actions?
- Should the generic page-selection query parameter be standardized, or should
  adapters keep domain-specific query names such as Resource Manager's `tab`?
- Which notification records belong in the Control Plane event stream, and
  which are UI-local shell state?
- Should shell layout configuration be stored in the UI host, the Control
  Plane, or a separate shell configuration service for split hosting?
- What is the minimum slot and section-container API that supports useful
  extension points without creating brittle page internals?
