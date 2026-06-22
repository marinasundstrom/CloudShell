# Shell composition

CloudShell UI should become an independently useful extensible shell, not only
the host for Resource Manager. Resource Manager remains the most important
first consumer, but the CloudShell Shell experience and extension model should
be general enough for provider workspaces, settings, notifications,
dashboards, and future product areas.

This proposal is about the CloudShell Shell layer. The reusable UI composition
library is its own subject, tracked by the
[UI composition library proposal](ui-composition.md). CloudShell Shell consumes
that lower-level library, defines stricter product areas on top, and projects
shell-owned contributions into the generic composition graph.

Eventually CloudShell should build the composition root into the core shell
layout. The main layout is where common shell chrome such as navigation,
topbar services, notifications, and page body rendering already meet, so it is
the natural place to load the composition engine, resolve the current route to
registered content, and cascade composition context to pages and nested
outlets. That is how integrating services will target shell-provided IDs
without each page wiring the engine independently.

The CloudShell Shell surface should be a product-level layout/content model
with CMS-like composition, not a tab engine or a raw navigation-menu API. It is
built for CloudShell experience and extensibility: the shell, built-in
capabilities, and installed extensions contribute through shell-owned contracts
that adapt to stable layout and content nodes in the lower-level graph.
Navigation hierarchy remains separate from content hierarchy. Menu nodes
describe how users navigate; content nodes describe pages, sub-pages, slots,
section containers, and sections. A menu item can target a content node by ID,
but it does not own the content hierarchy. Renderers then project the graph as
menu navigation, pages, sections, tabs, settings layouts, dashboards, or custom
extension-owned layouts. Resource Manager tabs are one adapter over this
model, not the generic model itself.

The shell model is analogous to the resource model at the shell/UI layer. The
shell owns a graph of stable artifacts and extension points. Modules
contribute declarations against that graph through CloudShell abstractions, and
the shell adapter validates ownership, target relationships, ordering, and
extension rules before renderers project the graph into UI. This keeps shell
artifacts separate from Blazor components in the same way resource model
concepts stay separate from provider implementation details.

The reusable Blazor integration should provide standard components that emit
plain HTML and are styled through classes. Component-framework-specific
presenters are host adapters over the same graph. CloudShell Hosting should
add Fluent UI presenters for shell navigation, settings, and Resource Manager
surfaces, while the standalone sample can continue proving generic behavior
with plain Bootstrap or custom CSS.

## Status

Proposed. This is post-MVP platform direction. MVP work should keep Resource
Manager and supported local-development samples stable while avoiding
short-term UI decisions that would prevent this model.

For the reusable library direction, see
[UI composition library](ui-composition.md). For the current experimental
library, CloudShell Hosting integration slices, and sample behavior, see
[UI composition](../../ui-composition.md).

## Layering Decision

CloudShell should split UI composition from the CloudShell Shell experience
and extensibility model. UI composition is an independent library subject;
CloudShell Shell is the product integration layer that consumes it.

`CloudShell.UI.Composition` is the lower-level structural engine. It owns the
permissive graph primitives: typed IDs, modules, pages, menus, section
containers, sections, target resolution, route metadata, renderer hints, and
future graph persistence. It is allowed to be broad because other Blazor host
applications can use it for their own dynamic layout and CMS-like composition.
Its direction belongs in the separate UI composition library proposal.

CloudShell Shell should own stricter product abstractions on top of those
primitives. Normal CloudShell integrations should not be expected to compose
raw pages, navigation nodes, and settings content independently unless they are
building a custom shell surface. They should target shell-owned contracts for
standard areas such as main navigation, the common Settings page,
notifications, provider workspaces, dashboards, and documented extension
areas. Those contracts project into the composition graph through CloudShell
adapters.

This gives CloudShell room to become more CMS-like without making the generic
composition library responsible for CloudShell product policy. The composition
library handles structure, relationships, routing metadata, renderer hints,
and eventual persistence. CloudShell Shell handles the ownership boundaries,
allowed targets, validation rules, extension activation, Fluent UI rendering,
accessibility, localization, user experience, and whether a page/sub-page
hierarchy is shown as tabs, side navigation, accordions, section stacks, or a
custom layout.

The main navigation should become a formal shell area with known IDs,
groups, ordering rules, permission behavior, and link resolution. The common
Settings page should become a formal shell area with a fixed outer layout and
a settings-specific contribution API that lets an extension register into the
hierarchical settings structure without separately managing both navigation
and routed content. Custom extension areas should also be shell-owned,
documented targets over composition slots or section containers, because the
generic composition engine does not yet define a CloudShell product policy for
those areas.

## Goals

- Split the reusable UI composition engine from the CloudShell Shell
  experience and extension APIs that sit on top of it.
- Let extensions contribute pages, sub-pages, slots, section containers,
  sections, menu entries, settings surfaces, notifications, and hosted
  workspaces through stable layout contracts.
- Keep the CloudShell UI deployable without the Control Plane. Resource
  Manager is an extension over the shell, not the definition of the shell.
- Keep the composition engine usable by Blazor host applications outside the
  built-in CloudShell UI, while making CloudShell UI the primary built-in
  consumer.
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
- Formalize shell-owned standard areas, starting with main navigation and
  Settings, so normal extensions integrate through durable CloudShell
  contracts rather than brittle separate menu and content registrations.
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
- Do not make the generic UI composition API the normal CloudShell extension
  API for product areas that need stricter shell-owned boundaries.
- Do not introduce a persisted layout editor before the programmatic contracts
  are stable.
- Do not build a composition editor UI in the reusable composition layer as
  part of the current direction. CloudShell or another host can build a CMS or
  editor experience on top of the infrastructure later; the near-term work is
  the programmatic graph, module boundaries, renderer primitives, extension
  integration points, and future persistence shape.
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

Dynamic composition is the core reason for the graph. Menus, section
containers, and other outlets do not hard-code their child components. They
ask the layout/content engine for the nodes declared by the shell itself and by
installed extensions, then render the matching components in order. A section
outlet, for example, dynamically loads the sections registered for its
container ID and current context.

Menus are hierarchical presenters of navigation. Menu items and sub-menu items
can point to the unique hierarchical ID of any addressable content node: a
page, sub-page, slot, section container, or section. The menu renderer does not
need to know whether the target is implemented as a Razor page, a tab, an
accordion section, a dashboard region, or a custom layout. It asks the
layout/content engine to resolve the target ID into a navigable link.

Menu composition should allow several modules to contribute to the same menu
or named menu group. The shell can own the main menu ID, while built-in
capabilities and extensions add items through that shared ID. The validated
runtime projection should merge those contributions for rendering, but still
preserve item-level module ownership for diagnostics, permissions, extension
activation, and future unload behavior.

Routing remains a separate concern from content registration. Razor components
can still declare routes through the normal `@page` directive. The
layout/content engine registers optional route metadata against a page content
ID so it can resolve links and deep links; it does not replace Razor routing or
make every content node routable.

The composition graph declares logical structure and URL projection. The
resolver turns that structure into links. The integrating UI framework remains
responsible for route handling and must honor the declared URLs when it defines
pages with its router. In Blazor, this can start with ordinary Razor pages and
later move to a CloudShell-owned composition-aware router component that maps
composition route metadata to Blazor route entries. The benefit of that router
is that composed pages would no longer have to duplicate route metadata in
`@page` directives; the composition graph could become the source of route
declarations while the Blazor integration handles matching and rendering.

Programmatic link resolution should start from typed composition targets such
as `PageId`, `SectionId`, `MenuItemId`, or an explicit href target. The
resolver materializes registered page route templates with supplied route
values, so a page registered as `/settings/{section?}` can resolve to
`/settings` or `/settings/platform` without callers concatenating strings. A
generic `SectionId` target should remain a content address that resolves to
the nearest routable page plus a fragment unless the host or renderer projects
that section as a route value under its owning page. `CompositeAnchor`,
menus, shell presenters, and custom renderers should all use the same resolver
contract.

The initial route projection should be convention-based: pages map to routable
addresses, page sections belong to their page address by default, and query
strings carry view-local state. Descriptors now carry address-mode metadata for
cases where a page, section outlet, or similar parent content scope declares
how its child sections are addressed. `Parent` means child sections share the
parent address and can be addressed inside that parent surface. `Child` means
each child section owns a child address value inside the parent scope. That
gives a formal way to address a logical group of sections without declaring
routing metadata on every section or exposing full section IDs in URLs. The
metadata still has to correspond to the routes the consuming Blazor page
declares and to the presentation layer's ability to map the URL back to
selected content. Address mode does not imply a specific renderer. Whatever
renders the parent decides whether a hierarchy of child pages or page sections
is visible as tabs, side navigation, segmented controls, accordions, or no
explicit nested navigation.

A later CloudShell host could introduce its own composition-aware Blazor router
component that registers route entries from this model instead of requiring
each routed Razor component to duplicate the same route shape. That should be
treated as a host routing adapter over the composition graph, not as a
requirement for the generic composition model or the first Blazor integration.
It should still keep component activation and authorization under host control
instead of letting arbitrary modules bypass shell validation.

The router should support two page participation modes. A
composition-rendered page is matched and rendered by the composition router
from registered route and component metadata. An externally routed page is
still matched by a normal Razor `@page` component, but that route is registered
against a composition page so menus, generated links, titles, breadcrumbs,
section outlets, and selected-child navigation remain graph-driven. This keeps
custom product pages in the system instead of forcing everything through one
generic renderer.

Resource Details is the key example. The routed Blazor component may continue
to own `/resources/{resourceId}` and `/resources/{resourceId}/{view?}` because
it needs a resource ID, resource loading behavior, authorization, and local
error states. The composition graph can still model the details page, its
resource-view children, and section outlets under those views. A menu item,
tab, breadcrumb, or deep link targets the composition page or child artifact;
the resolver produces a URL that the routed component must honor. A future
composition router can own simpler shell pages directly while externally
routed pages continue to opt into the same graph.

Before building that router, the model needs durable route primitives:
registered route templates on pages, page routing mode, selected-child route
values, section address mode, component host metadata, and route-conflict
validation. With those primitives in place, a router becomes an adapter over
the graph rather than the mechanism that defines the graph.

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

IDs should be typed value objects rather than interchangeable strings. They
are central to referencing artifacts and to describing hierarchy. The initial
model should include value objects such as `PageId`, `MenuId`,
`MenuGroupId`, `MenuItemId`, `SectionOutletId`, `SectionContainerId`, and
`SectionId`, or close equivalents as the API names settle. These IDs encode
the relevant navigation or content hierarchy in their value, while the type
keeps a menu ID from accidentally being used as a page or section ID. The
hierarchical value is the stable address; title, icon, ordering, permissions,
and component metadata remain separate registration metadata.

Presentation and policy attributes belong to artifact registrations,
descriptors, and projections. A menu item artifact can carry a title,
localization key, order, permission requirements, target, and an extensible
attribute dictionary for specialized fields such as icon. Those
attributes are not properties of the `MenuItemId` value type and are not
declared on the Blazor component type. The ID addresses the artifact; the
artifact metadata describes how that artifact should be presented, resolved,
or guarded by a host.

The durable descriptor shape should eventually include an extensible metadata
bag for artifact-specific fields. That keeps each artifact descriptor in a
standard envelope while allowing specialized renderers or modules to attach
additional values without changing every core type. Common characteristics
such as title, order, target, permission requirements, and localization
metadata should still be projected as first-class properties so ordinary menu,
link, settings, and section renderers can consume them without inspecting the
extension bag. Optional presentation details such as icons can use namespaced
attribute names such as `CompositionAttributeNames.Icon`.

Icons are intentionally outside the base menu item model. Different hosts may
represent an icon as a Fluent icon name, Bootstrap icon class, SVG asset,
image URL, CSS class, or another framework-specific token. The composition
framework should describe structure and layout: stable IDs, hierarchy,
ordering, targets, extension points, and generic metadata. Integrating
frameworks decide whether an attribute is meaningful and how to render it.
The current CloudShell shell presenter interprets `CompositionAttributeNames.Icon`
as a Fluent icon name; the standalone sandbox interprets the same attribute as
a Bootstrap Icons class.

Some IDs should be composed from parent IDs rather than assembled as unrelated
strings. For example, a `SectionId` should be built from a section identifier
and the parent page, sub-page, slot, or section-container ID it belongs to.
This keeps hierarchy explicit in the type system and lets builders create
stable child IDs from known parent artifacts. The string value remains useful
as a serialized address, but callers should normally construct IDs through
typed factories or builders instead of string concatenation.

Typed IDs also give the composition host an efficient indexing strategy. The
registry can keep separate maps for pages, menus, menu groups, menu items,
section containers, sections, routes, and target links without guessing the
artifact kind from an untyped string. The hierarchical value remains the
durable serialized address, while the value-object type selects the relevant
lookup map and prevents a renderer or extension from asking the wrong artifact
collection for an ID.

The graph should make these relationships explicit:

- a page belongs to a content hierarchy and may be bound to a Razor route
- a sub-page belongs to a page
- a slot belongs to a page, sub-page, or renderer-owned layout
- a section container belongs to a slot
- a section belongs to a section container
- a menu item or sub-menu item points to an addressable content node, command,
  route, external link, or another resolvable composition artifact
- a section, action, diagnostic, or generated link can point to an addressable
  content node by ID
- a settings section belongs to the standard settings page or to a settings
  category
- a Resource Manager tab adapts to a sub-page or section-container target while
  preserving resource-specific context
- a slot or section container declares the context and ordering rules for
  contributed content

Extensibility and replaceability are separate artifact policies. An
`IsExtendable` page, slot, or section container allows other modules to add
new child artifacts through published extension points. A future
`CanBeReplaced` policy would allow another module to replace an artifact,
renderer, or projection. Replacement needs stronger ownership, precedence,
diagnostic, and conflict rules, so it should not be treated as the same
capability as extension.

Each composition artifact should have separate API views for separate
responsibilities:

- declaration builders for the module that owns and first defines the artifact
- extension builders for other modules that are allowed to contribute to the
  artifact through a published extension point
- runtime projections for renderers, diagnostics, and consumers that need a
  mostly read-only view over the validated graph

Those views may point at the same underlying artifact ID, but they should not
expose the same mutation surface.

## Concept Vocabulary

The names below are working concepts for the sandbox and proposal. They are not
all current API names, but they describe the primitives the composition engine
should converge toward.

### Core Primitives

| Concept | Purpose | Notes |
| --- | --- | --- |
| Composition graph | The validated graph of registered navigation, content, metadata, and rendering relationships. | Built from programmatic registrations first; selected graph metadata may later be persisted. |
| Composition module | The owner of a registration, such as the shell, Resource Manager, a built-in capability, or a third-party extension. | Needed for diagnostics, conflict handling, trust boundaries, extension disable/unload, and future persistence. |
| Composition artifact | A registered thing in the graph. | Generic umbrella for pages, menu items, slots, section containers, sections, metadata, and future artifacts. |
| Composition ID | A typed stable address for an artifact. | Examples: `PageId`, `MenuId`, `SectionId`. The value encodes hierarchy, but the type prevents accidental misuse. |
| Composition target | A reference to an addressable artifact ID or a direct href. | Used by menu items, links, commands, diagnostics, and sections. The resolver turns artifact targets into a route, fragment, selected state, or not-found result, while href targets are emitted as links. |
| Composition context | Runtime context for the currently resolved content. | Starts with current page and route; should grow to include selected sub-page/section, route values, query state, ambient data, and caller/user state. |
| Composition metadata | Non-rendered facts attached to artifacts. | Title, description, icon, order, localization key, permissions, visibility, and module ownership belong here. |
| Composition descriptor | Serializable artifact data. | Descriptor objects should carry IDs, kind, ordering, owner/module, target relationships, route metadata, and serializable metadata for future dehydration and persistence. |
| Composition instance | Runtime artifact object. | Instance objects are projections over descriptors and runtime component bindings, after validation and module mounting. |
| Composition projection | Renderer-ready view over artifacts. | Menus, tab sets, section lists, breadcrumbs, and settings layouts should consume projections rather than raw mutable builder state. |

### Navigation Primitives

| Concept | Purpose | Notes |
| --- | --- | --- |
| Menu | A named navigation presenter. | A shell can render several menus, but menu hierarchy is not the content hierarchy. |
| Menu group | A named group of menu items inside a menu. | Useful for sidebar groups such as Workspace, Observability, Platform, or Settings. Menu items may also live directly under the menu when grouping is unnecessary. |
| Menu item | A navigation node targeting a composition target, command, route, or external link. | It should not directly own routed content. |
| Navigation renderer | A component or service that turns menu artifacts into UI. | The default can be simple; hosts can build Bootstrap, Fluent, compact, or custom navigation renderers. |

### Content Primitives

| Concept | Purpose | Notes |
| --- | --- | --- |
| Page | Addressable content normally bound to a Blazor route. | Razor owns route matching; composition records the route-to-page relationship. |
| Sub-page | Addressable content selected inside a page. | Can back tabs, local navigation, accordions, or query-parameter selected views. |
| Slot | A named placement point declared by a page or renderer. | A slot describes where content can be placed and what context/content kinds it accepts. |
| Section container | An ordered container for sections inside a slot or page. | Current implementation uses `SectionOutletId`; naming may settle on section container or outlet. |
| Section | A contributed content component rendered inside a section container. | Sections are useful for extension-owned additions without replacing a whole page. |
| Content outlet | Runtime component that renders content registered for the current context and target. | Current `CompositionSectionOutlet` is one concrete outlet. |

Named sections are the first implemented content primitive in the experimental
libraries. A named section carries a stable ID, display title, order, and
component type. Renderers such as stacked section outlets, dashboard grids, and
tabs should consume that same section registration instead of inventing
renderer-specific content registrations.

Dashboard customization should be modeled as composed content, not as a
dashboard-specific plugin API. A dashboard can declare slots or section
containers for custom content areas, and installed modules can contribute
sections into those containers while the dashboard renderer chooses the final
grid or tile layout. If a module needs a whole custom surface, it should
register a page and map a menu item to that page through a composition target.
That is the extension point for embedded operational tools, including a
provider-owned Grafana page, without making the built-in Metrics explorer own
third-party dashboard behavior.

### Rendering Primitives

| Concept | Purpose | Notes |
| --- | --- | --- |
| Composition host | Bridges Blazor routing and composition context. | Usually lives in a layout and cascades the current `CompositionContext`. |
| Layout pattern | A reusable way to present composed content. | Examples: section stack, dashboard grid, tabbed page, master-detail, settings page, wizard, split pane. |
| Layout renderer | Component that implements a layout pattern over the composition graph. | Should be host-owned or package-owned; the core engine should not assume one visual metaphor. |
| Section renderer | Component that decides how section metadata and section component content are framed. | A section stack might use article cards; a dashboard grid might use responsive tiles. |
| Metadata outlet | Component that renders metadata for the current context. | `TitleOutlet` renders visible title text and `PageTitleOutlet` wraps Blazor `PageTitle` for the document title. Future outlets may render breadcrumbs, descriptions, icons, actions, or other document head metadata. |
| Link resolver | Service that turns a composition target into a navigable address. | It should support page routes, sections/fragments, selected sub-pages, missing targets, disabled targets, and future deep links. |

### Adapter Primitives

| Concept | Purpose | Notes |
| --- | --- | --- |
| Product adapter | Maps a domain-specific contribution model into composition artifacts. | Resource Manager tabs and predefined sections should remain Resource Manager concepts, then adapt into the generic graph. |
| Extension adapter | Maps CloudShell extension registrations into composition artifacts. | This comes after the standalone composition shape is credible. |
| Persistence adapter | Loads and stores selected graph metadata. | Should not persist executable component types as arbitrary data. It should preserve module ownership and validate against installed code-owned capabilities. |

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
while still projecting from the generic graph. New navigational surfaces
should prefer product-shaped path routes where the selected content is a stable
location. Resource Manager follows that convention with
`/resources/{resourceId}` for the Resource Details container page and
`/resources/{resourceId}/{view}` for a selected Details tab/view instead of
exposing the full view ID in a query parameter.

URL shape should remain a host and adapter decision rather than a direct dump
of the full hierarchical ID. The ID is the durable internal address used for
lookup, ownership, diagnostics, and extension targeting; it is not always the
best human-facing navigation path. The current convention is deliberately
opinionated: pages use routable addresses, while a parent renderer may project
child pages or child sections as nested navigation when the selected content is
a stable location that users should bookmark, share, or understand as part of
the information architecture, such as `/settings`, `/settings/identity`,
`/resources/{resourceId}`, or `/resources/{resourceId}/{view}`. Parent-addressed
sections can still be materialized as focus targets or in-page anchors. Query
parameters carry state inside an already-established page context, such as a
selected trace, filter, sort, or temporary view mode. A resolver can still map
all of those URL forms back to composition IDs so menus and links stay
ID-driven while URLs remain intentional.

Until a custom mapping facility exists, public child address values are derived
by convention from the addressable artifact's local identifier in its parent
scope. For example, a typed Resource Manager view ID such as
`management:access-control` keeps its full internal address, but resolves to
the `access-control` route value under `/resources/{resourceId}`. Hosts must
validate that child address values are unique within their parent scope.

Section outlets carry an explicit section address mode on the parent scope.
The default remains `Parent`: child sections share the current parent address.
A page, section outlet, or similar section container can opt into `Child` when
child sections should own child address values, such as settings categories or
Resource Details views. The public child address value should be short and
product-shaped, while the `SectionId` remains the durable internal address used
for extension targeting, authorization, ownership, and diagnostics. Resource
Details is the practical example: `/resources/{resourceId}` is the parent page,
the Resource Details renderer chooses an inline navigation presentation, and
the relevant section outlet opts into child addresses so selected sections
resolve as `/resources/{resourceId}/{view}` instead of the default
parent-addressed fragment form.

The content selected inside a page or view may be dictated by route values,
fragments, and page-local state. The route establishes the page context and
stable child-address selections. Fragments can target content inside that
page. Query parameters should remain page-local state, such as filters,
selected telemetry records, sort order, or temporary view modes, rather than
the default representation for stable tabs or sub-pages. The composition
engine should normalize address state back to content IDs where possible so
links, menus, tabs, section outlets, and custom renderers all resolve the same
target consistently.

Projection remains the consumer's responsibility. The composition model can
describe a page with subordinate sub-pages, but a host component decides
whether those sub-pages render as tabs, side navigation, cards, accordions, or
another local navigation pattern. Resource Manager resource views are a good
example: each view can logically be treated as a sub-page under the Resource
Details page, while the current Resource Manager renderer presents those
sub-pages as grouped tabs.

## Near-term implementation constraints

The experimental libraries intentionally implement only a small subset of the
proposal:

- Page, menu, menu item, section outlet, and section IDs are typed value
  objects.
- Current IDs wrap stable string values and include first-pass factories for
  composing child IDs, such as section IDs, from an identifier plus a parent
  artifact ID.
- Sections are the current named content primitive.
- The Blazor package provides plain renderers for menus, links, titles,
  stacked sections, and section tabs.
- `CompositionEngineHost` provides an in-memory mounted-module list and
  rebuilds the active registry projection when a module is mounted or
  unmounted.
- The registry exposes first-pass page, menu, and section projections that
  preserve the owning `CompositionModuleId`.
- Plain Blazor menu and section renderers consume those projections and expose
  the owning module as `data-composition-module` attributes for diagnostics.
- Section outlets are explicit artifacts with `IsExtendable`. The registry
  rejects cross-module section contributions unless the target outlet is
  extendable. Permissions and visibility remain a future dynamic layer over
  registration and rendering.
- Modules and runtime registrations can be projected into descriptor records
  that JSON round-trip. Module descriptors can be rehydrated into runtime
  modules through a host-provided component type resolver.
- Registration titles are plain strings. Localization metadata, localization
  providers, and title content templates remain future design work.
- Registration ownership is recorded at the module boundary through
  `CompositionModuleId`, and basic artifact projections preserve that module
  owner. Richer renderer diagnostics remain future work. CloudShell extension
  discovery and activation/deactivation rules for deciding when modules mount
  or unmount also remain future adapter work. That richer module metadata will
  be needed for diagnostics, disable/unload behavior, conflict handling, trust
  boundaries, permission review, and future persisted graph metadata.
- Runtime artifact instances and renderer projections are not yet separated
  from descriptor data. The direction is to keep the new serializable
  descriptors as the durable artifact shape, produce runtime instances from
  those descriptors through a host-owned resolver, and expose
  renderer-specific projections from the validated graph.

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
| Menu | A stable top-level navigation presenter encoded by ID, title, order, permissions, optional visibility rules, and namespaced attributes for specialized presentation metadata. |
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

An extension-ready composition model will also need to record which module
registered each artifact. A future `CompositionModule`, or equivalent value
object, can identify whether a menu item, page, section container, section,
slot, or metadata contribution belongs to the main shell module, Resource
Manager, a built-in capability, or a third-party extension module. Module
ownership is separate from visual hierarchy and content hierarchy. It gives
the host a durable way to explain where content came from, validate conflicts,
apply permissions and trust boundaries, support extension unload/disable
behavior, and later persist selected graph metadata without losing ownership.

For example, a page may live under the shell content hierarchy while being
owned by the Resource Manager module, and a section inside that page may be
owned by a SQL Server provider module. The composition registry should be able
to preserve that ownership chain even when all artifacts render through the
same menu, page, and section outlet primitives.

## Reusable composition engine investigation

CloudShell already has two similar UI composition paths:

- Shell-hosted views use `CustomShellViewContribution` and
  `CustomShellViewMenuItemContribution`. They provide one hosted route, an
  ordered local menu, selected-item routing, and dynamic component rendering.
- Resource Manager uses `ResourceTabContribution`,
  `ResourcePredefinedViewSectionContribution`, `ResourceViewId`, generated
  fallback tabs, predefined view validation, resource-tab grouping, selected
  tab routing, and dynamic component rendering with resource context
  parameters.

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

The future stack should keep the reusable UI composition library and the
CloudShell Shell product layer separate:

| Layer | Owns | Examples |
| --- | --- | --- |
| UI composition core | Generic hierarchical IDs, node kinds, relationships, navigation nodes, pages, sub-pages, slots, section containers, sections, ordering, generic authorization metadata, routing metadata, selection state, target resolution, descriptor projection, and component host metadata. | `CloudShell.UI.Composition`, typed IDs, `CompositionRegistry`, graph validation |
| UI composition Blazor integration | Plain Blazor components and context integration with no dependency on a visual component framework or CloudShell product model. | `CloudShell.UI.Composition.Blazor`, `CompositionHost`, `CompositionMenu`, `CompositeAnchor`, `CompositionSectionOutlet` |
| CloudShell Shell abstractions | Product-owned shell areas, allowed extension targets, stable IDs, permission policy, localization boundaries, settings hierarchy, main navigation rules, notification surfaces, and shell extension validation. | Shell main nav contracts, Settings contribution contracts, notification contracts, provider workspace contracts, documented extension areas |
| CloudShell Shell adapters | Projection from shell-owned product abstractions into UI composition artifacts while preserving ownership and diagnostics. | Shell navigation projector, settings projector, Resource Manager composition adapter, notification center adapter |
| CloudShell Shell presenters | Fluent UI rendering, grouped local navigation, URL selection, dynamic component rendering, empty/not-found states, permission-aware visibility, accessibility, and slot/section presentation. | Hosted workspace layout, settings layout, Resource Manager detail layout, dashboard layout |
| Domain-specific adapters | Domain-specific contribution APIs that project into shell abstractions or directly into composition only when the domain owns a custom shell surface. | Resource Manager tabs, predefined resource view sections, provider settings, observability workspace content |
| Domain services | Control Plane or provider behavior behind the UI. | `IResourceManager`, `ITraceManager`, identity provider hooks, provider settings contracts |

This lets Resource Manager reuse the same layout engine as other shell pages
without leaking resource-specific vocabulary into general shell APIs.

The Blazor integration should work with normal Blazor components and the
standard Blazor app model. For example, the standalone sandbox uses Blazor's
built-in `HeadOutlet`. CloudShell may later add shell-aware metadata outlets,
starting with at least a plain `TitleOutlet` for visible titles and a
`PageTitleOutlet` that respects Blazor's normal `PageTitle`/`HeadOutlet`
pipeline for document titles. Those outlets should be additive shell
integration components rather than requirements for the base composition
engine.

The Blazor component package should be render-mode neutral. Base renderers
should work under static SSR, interactive server, WebAssembly, and mixed
render modes by producing ordinary HTML links and markup, keeping core
selection in routable state, and avoiding JavaScript or event-handler
requirements for navigation. Cascaded composition context remains useful for
normal layouts, but components that render page metadata or page-scoped
sections should also accept explicit page IDs or resolve the current route so
hosts can place them in render-mode islands without depending on cascade
propagation across every boundary.

Permission-aware Blazor renderers should align with normal Blazor
authorization instead of inventing a parallel rendering model. Composition
artifacts carry authorization metadata, including CloudShell permission names
and neutral policy, role, and claim requirements. Renderer projections expose
that metadata. A Blazor renderer can then wrap menus, pages, section outlets,
or individual sections in `AuthorizeView`, using its normal `Roles`, `Policy`,
`Authorized`, and `NotAuthorized` behavior, or can map the same requirements to
a host-specific authorization service. Hosts should decide whether
unauthorized content is hidden, disabled, replaced by a not-authorized
template, or surfaced as a diagnostic in development. This keeps
`IsExtendable` as a structural contribution rule, while authorization remains
a dynamic visibility/access concern owned by the host's configured
authentication and authorization system.

Builder APIs should keep adding convenience methods for common requirements,
such as `RequirePolicy(...)`, `RequireRole(...)`, and `RequireClaim(...)`,
without hiding ASP.NET Core authorization. Renderer-side projection helpers can
then translate that metadata into `AuthorizeView` parameters, authorization
service checks, or development-time diagnostics that explain why a page, menu
item, section outlet, or section is hidden or blocked. CloudShell-specific
permission concepts should map into these policy, role, and claim requirements
at the host adapter layer.

## Resource Manager extraction path

The practical path should be incremental:

1. Introduce the layout graph vocabulary and registry for the separate
   navigation and content hierarchies: menus, menu item groups, menu items,
   sub-menu items, pages, sub-pages, slots, section containers, and sections,
   without replacing existing Resource Manager tabs or shell view menu items
   yet.
2. Prove the model in a clean standalone Blazor app before adding CloudShell
   extension integration. The sample should reference only the composition
   libraries, use normal Blazor routing and layout, and demonstrate that plain
   Bootstrap CSS can style the generic Blazor components without requiring a
   UI framework package.
3. Define a CloudShell extension adapter after the standalone structure is
   credible. The adapter should map CloudShell extension contributions into
   the core composition graph rather than making the composition engine depend
   on the CloudShell extension model.
4. Register composition services in CloudShell UI and allow extensions to add
   passive composition modules before any shell renderer depends on them. This
   gives CloudShell an integration seam while the current shell catalog,
   navigation, and Resource Manager views continue to render normally.
5. Add shell-owned layout components that consume the composition graph beside
   the existing shell catalog. These components should prove menu, page,
   section, and outlet projections inside CloudShell without forcing an
   immediate menu-system migration.
6. Gradually move shell navigation to composition-backed menus, identify
   missing metadata, selection, localization, icon, permission, grouping,
   parent/child, and projection APIs, and fill those gaps in the reusable
   composition layer where they are generic. Fluent UI-specific rendering
   should live in CloudShell Hosting presenters rather than the reusable
   composition packages.
7. Move the composition root into the core CloudShell main layout once the
   composition app and adapter prove the model. The core layout should resolve
   current route/page context and cascade composition context to shell chrome,
   routed pages, and nested outlets so integrating services can target
   shell-provided IDs.
8. Dogfood the graph with a standard Settings page made from shell pages,
   sub-pages, slots, section containers, and sections. The initial settings
   experience can render as tabs, but the contract should remain layout-node
   based rather than tab based.
9. Move Resource Details to the same shell-owned tabbed layout pattern after
   the settings page proves the layout component. Resource Manager tab
   contributions should remain Resource Manager concepts until an adapter maps
   them into composition artifacts.
10. Extract a generic ordered-section renderer from
   `GeneratedResourceViewLayout` and `ResourcePredefinedViewSections`, keeping
   the current Resource Manager section contracts as adapters.
11. Extract a generic grouped local-navigation renderer from the Resource
   Manager tab grouping and the shell-hosted view menu item model. Treat tab
   rendering as one renderer over child layout nodes.
12. Let `CustomShellViewContribution`, the new Settings page, and Resource
   Manager detail pages render through the same hosted-page/layout graph
   infrastructure where their needs overlap.
13. Add generic slot and section-container contributions and map
   `ResourcePredefinedViewSectionContribution` into section contributions for
   predefined resource views.
14. Add notification-center and dashboard adapters over the same primitives.
15. Only after the generic renderer is proven, consider renaming or replacing
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

The examples below are conceptual and describe how the lower-level UI
composition graph may be assembled. They are not necessarily the public
CloudShell Shell extension API for normal integrations. CloudShell-owned shell
contracts should wrap these primitives for standard product areas so extension
authors can target main navigation, Settings, notifications, provider
workspaces, and documented extension areas without coordinating raw menu and
content registrations themselves.

The generic contracts should be small and preview-marked at first. The final
API names and overloads need design work. IDs should be value objects rather
than plain strings at public boundaries so extension authors can target known
layout nodes without relying on ad hoc string conventions.

The host application or built-in shell capabilities define the initial
navigation and content structure through builders:

```csharp
var mainMenu = builder.AddMenu(PredefinedMenus.Main);

var resourcesItem = mainMenu.AddItem(PredefinedMenuItems.Resources)
    .Target(PredefinedPages.Resources);

resourcesItem.AddItem(PredefinedMenuItems.ResourcesAll)
    .Target(PredefinedPages.ResourcesAll);

var observabilitySection = mainMenu.AddSection(PredefinedMenuItemGroups.Observability);

observabilitySection.AddItem(PredefinedMenuItems.Traces)
    .Target(PredefinedPages.Traces);

var settingsPage = builder.AddPage(PredefinedPages.Settings, isExtendable: true);

var settingsSections = settingsPage.AddSections(
    PredefinedSectionContainers.Settings,
    isExtendable: true);

settingsSections.AddSection(
    PredefinedSections.SettingsGeneral,
    component: typeof(GeneralSettingsSection));
```

An extension that contributes a custom shell surface may eventually receive a
`CompositionModuleBuilder`, or equivalent builder scoped to its extension
module. Standard areas should prefer shell-specific builders instead. The
underlying module builder creates a `CompositionModule` containing the
extension-owned descriptors, and the CloudShell extension framework mounts or
unmounts the resulting module according to extension activation rules:

```csharp
public sealed class IdentityExtension : ICloudShellExtension
{
    public void Compose(ShellCompositionHostContext context, CompositionModuleBuilder module)
    {
        module
            .Extend(context.Settings.MainSections)
            .AddSection(
                MySections.IdentityProviders,
                component: typeof(IdentityProviderSettingsSection));
    }
}
```

Once a module is built, the composition engine host can mount it at the
appropriate time. Mounting does not require application startup; it can happen
whenever the shell or extension framework decides the module is available.
Unmounting removes or disables that module's artifacts and causes projections
to be rebuilt.

An extension can then tap into explicitly extendable nodes and add its own
content without owning the surrounding layout:

```csharp
var identitySettings = builder.Extend(context.Settings.MainSections);

identitySettings.AddSection(
    MySections.IdentityProviders,
    component: typeof(IdentityProviderSettingsSection));

builder.GetMenu(PredefinedMenus.Main)
    .GetSection(PredefinedMenuItemGroups.Observability)
    .AddItem(MyMenuItems.IdentityAudit)
    .Target(MyPages.IdentityAudit);
```

The module that owns a page, slot, or section container must publish strongly
typed references for the elements that other modules are allowed to target.
Cross-module contributors receive the relevant composition host context from
the module registration API and use those references to request an extension
builder for the known layout element. They do not redeclare the outlet. The
composition host validates that the target exists and is marked extendable
when mounted modules are assembled.

`Extend(...)` should return builders constrained to the operations that make
sense for the target artifact. Extending a page can return a page extension
builder that adds extension-owned section containers or outlets, and the host
must validate that the page is extendable. Extending a section should wait
until section-owned child outlets and their parent page context are explicit in
the model; a section ID alone is not enough information for a separate module
to safely add child content.

Navigation can target any addressable content node without becoming its
parent. A menu item can point at a page, sub-page, section container, or
section by ID:

```csharp
mainMenu.AddItem(MyMenuItems.IdentityProviderSettings)
    .Target(MySections.IdentityProviders);
```

The content engine resolves the target content ID to the nearest routable page
and renderer-specific selected state, fragment, or focus target.

Razor components still declare routes through normal Razor routing. A
composition host component binds the routed component to a content ID so the
layout/content engine can load the matching context, attach metadata, cascade
that context to nested composition areas, and render navigation or deep links
by ID. The component may not ultimately be named `Page`; the important role is
that something in the routed component hosts the composition context.

```razor
@page "/test/{title}"

<CompositionHost Id="@MyPages.Test">
    <Menu Id="@PredefinedMenus.Main" />

    <SectionContainer Id="@MySectionContainers.TestMain">
        <SectionOutlet />
    </SectionContainer>
</CompositionHost>

@code {
    [Parameter]
    public string? Title { get; set; }
}
```

The composition host is the bridge between Razor routing and the content
engine. The implementation can inject the layout engine into the host, resolve
the content context from the supplied ID plus route, fragment, and page-local
state, and cascade that context to `Menu`, `SectionContainer`,
`SectionOutlet`, and custom renderer components. A standard menu component can
then render the
hierarchical navigation tree and ask the content engine to construct links
from target content IDs. A section outlet can render all sections registered
for the current section container, including sections declared by extensions.
It should know which page or sub-page it belongs to by relying on the cascaded
composition context, rather than requiring every outlet invocation to repeat
the full page hierarchy. The outlet can combine the cascaded page context with
its own section-container ID, or the nearest container context, to resolve the
declared sections that apply at that point in the hierarchy.

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

## Persistence Direction

The first composition model is programmatic and in-memory. That keeps the
initial engine focused on the graph shape, routing bridge, and rendering
contracts. A later CMS-like experience may persist parts of the composition
graph in a database, but that persistence should target the core composition
facilities rather than the Blazor integration layer.

Persistable composition state should be limited to durable graph metadata such
as layout IDs, ownership, ordering, visibility, target relationships, and
route/deep-link metadata. Component types, service wiring, extension
capabilities, and permission checks remain code-owned integration concerns.
That split would let CloudShell support curated or admin-configured layouts
without turning arbitrary database rows into executable UI components.

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

The composition model should not try to solve every CloudShell settings
problem directly. CloudShell can build settings-specific abstractions on top of
the generic graph when a contribution needs to register content and matching
navigation projections together. Until that abstraction exists, the common
Settings page can project the content hierarchy directly and use artifact
metadata, such as grouping attributes, as renderer hints. For example, Resource
Management settings can contribute separate General and Orchestration sections
under one common settings outlet and let the CloudShell settings renderer group
them without declaring a separate menu.

## Notifications

CloudShell should provide shell-owned notification presentation and a common
notification shape, but the shell should not necessarily own durable
notification storage. The shell owns the user-facing contract: notification
records, severity, source metadata, targets, action affordances, presenter
behavior, user-facing interaction patterns, and the interfaces the
notification UI uses to fetch current notifications and receive updates.
Product areas that use the shell, such as Resource Manager, own the
operational logic that produces notifications and can plug in their own
persistence or delivery services when they need durable history.

The stable abstraction should be a notification source. A source is the owner
of a stream or collection of notifications, such as a future Resource Manager
notification source backed by resource events, lifecycle procedures, health
state, provider diagnostics, and asynchronous task results. The shell can later
aggregate several sources into one notification center, but aggregation should
not erase source ownership or force all producers through one store.

The system should provide two presentation modes:

- Toasts for transient, immediate feedback.
- An off-canvas notification center for durable notification history,
  unread state, and operator review.

Notification records should identify their source extension or product area,
severity, timestamp, optional resource or route target, optional action
affordances, and whether the notification is transient, durable, or backed by a
source-specific history. Resource lifecycle events, permission denials,
provider diagnostics, and long-running asynchronous operation results can feed
this system without every feature building its own notification UI.

The shell can define contracts for notification sources, unread state,
dismissal, action dispatch, and update subscriptions, but implementations
should live with the area that owns the events. Resource Manager might expose a
source backed by resource events, lifecycle procedures, health transitions, or
provider diagnostics. Observability could expose a source for alerts or
degraded-service findings. Another shell extension might provide a completely
different source. Each source can decide whether history is transient,
in-memory, Control Plane-backed, provider-backed, or
external-service-backed.

CloudShell Shell should not prescribe the data flow behind those providers. A
notification source can poll its backing service, listen to Control Plane or
provider events, maintain an event aggregator, project an existing event store,
or call an external service. The shell UI should only depend on the source
interfaces for listing notifications, receiving change notifications, marking
items read or dismissed, and dispatching declared actions. That avoids forcing
CloudShell to decide how every consumer pushes updates into a shared in-memory
store.

Durable notification storage should therefore be pluggable. A source that
needs durable history can expose a provider that tracks payloads, audience,
delivery state, read/dismissed state, and timestamps. A source that only needs
toasts can publish transient records with no persistence. Fan-out should also
belong to the source or backing service that understands the audience:
explicit users, roles, policies, claims, resource ownership, environment
membership, subscriptions, or another product-specific rule.

The composition model should not become the notification domain model. It can
provide addressable pages, notification-center layout areas, and extension
contribution points, while CloudShell Shell owns the shared notification shape
and Fluent UI presenters. Product areas plug in notification sources and
optional stores behind that shape. CloudShell can provide reference
implementations, such as a simple in-memory source and a default persistent
source for hosts that want local notification storage. A common aggregate store
can also be useful in simple deployments, but it should be an implementation
choice based on ownership and use-case requirements rather than a shell
requirement.

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
  pages, settings sections, and notification actions, and how should Blazor
  renderers project those requirements through `AuthorizeView`? Which helper
  APIs should exist for common role, policy, and claim requirements?
- What shape should a future custom URL mapping facility take when convention
  based child address values are not enough, and how should pages or section
  outlets customize the projection of parent-addressed or child-addressed
  sections?
- Which stable notification source abstractions should Resource Manager
  provide from Control Plane events, lifecycle procedures, health transitions,
  provider diagnostics, and asynchronous task results?
- What is the minimum shell notification source contract for transient toasts,
  durable history, unread state, dismissal, update subscription, and action
  dispatch without requiring the shell to own persistence or an in-memory
  update store?
- How should a source-owned notification store declare its audience and
  fan-out model: explicit users, roles, policies, claims, resource ownership,
  environment membership, subscriptions, or a product-specific rule?
- Which reference implementations should CloudShell provide, such as
  in-memory, persistent, or aggregate-source providers, and which should remain
  Resource Manager-owned or extension-owned?
- Should shell layout configuration be stored in the UI host, the Control
  Plane, or a separate shell configuration service for split hosting?
- What is the minimum slot and section-container API that supports useful
  extension points without creating brittle page internals?
- What is the right `CompositionModule` shape for built-in shell areas,
  Resource Manager adapters, capability packages, and third-party extensions?
- What is the exact descriptor/instance/projection split for component-backed
  artifacts so descriptors remain serializable without leaking runtime-only
  component state into persistence?
- Which additional ID factories are needed so composed child IDs remain
  ergonomic without hiding hierarchy from extension authors?
- Should module unmount remove artifacts from the active graph immediately, or
  mark them unavailable so deep links can produce better diagnostics?
