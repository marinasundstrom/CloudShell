# UI Composition

CoreShell composition is an experimental layout/content substrate for shell
structure. It stores low-level structure and relationships between content.
It started outside CloudShell Hosting and Resource Manager so the basic graph,
routing bridge, and rendering components could be proven in a normal Blazor
app before adding CoreShell and CloudShell-specific adapters.
CloudShell Hosting now consumes the same libraries for selected shell surfaces
while the standalone sample remains the place to prove layout patterns without
coupling them to the shell.

For future direction, see the
[UI composition library proposal](proposals/core/ui-composition.md). For the
CloudShell product layer built on top of the library, see the
[shell composition proposal](proposals/core/shell-composition.md).

## Packages

The current implementation is split into two libraries:

| Library | Current role |
| --- | --- |
| `CoreShell.Composition` | Core facilities: typed IDs, page/menu/section registrations, module building, registry builder, route lookup, section ordering, and target-to-link resolution. |
| `CoreShell.Composition.Blazor` | Plain Blazor components and DI registration for rendering the core graph. It does not depend on Fluent UI, Bootstrap component packages, CloudShell Hosting, Resource Manager, or CloudShell extension contracts. |

The Blazor components render ordinary HTML elements and can be styled by the
host app with normal CSS. The `samples/CompositionSandbox` app demonstrates
this with plain Bootstrap CSS and a small app stylesheet.

Framework-specific presentation belongs in host adapters. The base Blazor
package should keep standard, render-mode-neutral components that emit normal
HTML and expose class parameters. CloudShell Hosting can add Fluent UI
presenters for the same composition projections, and another host can add
Bootstrap or custom presenters without changing the core graph.
Shell integrations should target CoreShell services and contracts rather than
these composition primitives directly. Direct use of
`CoreShell.Composition` is reserved for CoreShell infrastructure, presenter
packages, compatibility adapters, and sandbox hosts. Domain shells such as
CloudShell can build their own extension points on top of CoreShell.
CoreShell adds the shell-facing layer above this graph: pages, navigation,
sections, content references, layout references, target resolution, and page
materialization services. The Blazor-specific mapping from CoreShell content
or layout references to component types belongs in the host adapter, not in
the foundational CoreShell contracts.
CloudShell's primary navigation renders from CoreShell services; direct
composition registry access remains an infrastructure and compatibility path
while page and section presenters move up to CoreShell.
Shell links that target CoreShell pages or targets should use the CoreShell
anchor adapter. Composition anchors remain for compatibility with presenters
that still consume composition page and section IDs directly.
Resource Manager page and detail links should use the CoreShell-backed
Resource Manager shell link helper. The remaining composition link helper is
kept for presenters that still build tab and section state from composition
projections.
Because CloudShell Hosting is a Fluent UI shell, shell-specific presenters
should use Fluent UI components for navigation, commands, inputs, and other
interactive elements whenever those components provide the needed semantics and
styling hooks. Drop to plain HTML only when Fluent does not expose the required
behavior, render-mode shape, or layout flexibility. The reusable Blazor
composition package remains the exception: it intentionally stays plain and
framework-neutral so non-Fluent hosts can consume the same graph.

Render-mode neutrality is a design objective for the Blazor integration. The
base components should work when a host uses static SSR, interactive server,
WebAssembly, or mixed render modes. They should render normal links and
markup, avoid JavaScript and event-handler requirements for core navigation,
and keep selected state in routable state such as query strings or fragments
when possible. Components can consume cascaded composition context, but routed
outlets and metadata components should also be able to resolve the current
page from an explicit `Page` parameter or the current route so they still work
when an app places an interactive island across a cascade boundary.

## Core Model

The core graph uses typed value objects instead of raw strings at the public
boundary:

```csharp
new MenuId("menu.main");
new MenuItemId("menu-item.main.workspace");
new PageId("page.workspace");
new SectionOutletId("section-outlet.workspace.main");
new SectionId("section.workspace.main.overview");
```

The ID value is the stable address. The type prevents accidentally using a
page ID where a menu ID or section outlet ID is expected.

The value types can also compose child IDs from parent IDs:

```csharp
var workspacePage = PageId.Create("workspace");
var mainOutlet = SectionOutletId.Create(workspacePage, "main");
var overviewSection = SectionId.Create(mainOutlet, "overview");
```

This produces stable hierarchical values such as
`section-outlet.workspace.main` and `section.workspace.main.overview` without
requiring callers to concatenate strings by hand. The constructors remain
available for predefined or migrated IDs.

The current graph supports:

- composition modules
- menus
- menu groups
- menu items and sub-items
- pages bound to normal Blazor routes
- section outlets
- named sections rendered into a section outlet
- page and section targets resolved into links

Navigation hierarchy and content hierarchy are already separate. A menu item
targets either an addressable composition artifact by ID or a plain href. It
does not own the content behind that target.

Typed hierarchical IDs also let the registry keep kind-specific lookup maps.
Page, menu, section outlet, and section queries use typed IDs, while target
resolution uses the same stable ID values for case-insensitive page and
section link lookup. This keeps renderers and future adapters from scanning
all artifacts or inferring artifact kind from loose strings.

Section outlets are explicit artifacts. A page can mark itself
`IsExtendable`, which means other modules may add extension-owned section
outlets to that page. A section outlet can also be marked `IsExtendable`,
which means other modules may contribute named sections to that outlet. Named
sections are the current content contribution primitive. A section has a
stable `SectionId`, a display title, an order, and a component type. Renderers
can show those same sections as a stack, grid, tabs, or another layout pattern
without changing the registered content graph.

The current registry stores runtime registrations directly, but modules and
registrations can be projected into descriptor records. Descriptors are the
first serializable shape for pages, menus, menu groups, menu items, section
outlets, and sections. Section descriptors store the component type
name rather than a runtime `Type`. A module descriptor can be rehydrated through
`CompositionModule.FromDescriptor(...)` only when the caller supplies an
`ICompositionComponentTypeResolver`, keeping component activation under host
control.

Composition artifacts have different API views depending on who is using
them:

- declaration builders are used by the owning module when it first defines the
  artifact and can expose the full definition surface
- extension builders are used by other modules through published extension
  points and expose only the operations that are allowed for that target
- runtime projections are consumed by renderers and diagnostics and are mostly
  read-only views over validated registrations, ownership, and metadata

## Registration

Host applications can register the composition graph during startup. For a
single host-owned graph, the simple builder overload is still available:

```csharp
builder.Services.AddCloudShellUiComposition(composition =>
{
    var mainMenu = composition.AddMenu(CompositionIds.MainMenu, "Main");

    mainMenu
        .AddItem(CompositionIds.WorkspaceItem, "Workspace", 10)
        .Target(CompositionIds.WorkspacePage);

    mainMenu
        .AddItem(CompositionIds.ReportsItem, "Reports", 20)
        .Target(CompositionIds.ReportsPage);

    var workspacePage = composition.AddPage(
        CompositionIds.WorkspacePage,
        "Composition workspace",
        "/");

    workspacePage
        .AddSections(CompositionIds.WorkspaceMainOutlet, isExtendable: true)
        .AddSection<OverviewSection>(
            CompositionIds.OverviewSection,
            "Composition root",
            10);

    composition
        .GetSections(CompositionIds.WorkspaceMainOutlet)
        .AddSection<ExtensionContributionSection>(
            CompositionIds.ExtensionContributionSection,
            "Contributed section",
            20);

    composition
        .AddPage(CompositionIds.ReportsPage, "Composition reports", "/reports")
        .AddSections(CompositionIds.ReportsMainOutlet)
        .AddSection<ReportsSummarySection>(
            CompositionIds.ReportsSummarySection,
            "Page navigation",
            10);
});
```

Modules are the first implementation step toward extension-owned composition.
A module records the pages, menus, and sections produced by a module-scoped
builder. A registry can then be assembled from one or more modules:

```csharp
var hostModule = CompositionModule.Create(
    CompositionModuleId.Host,
    module =>
    {
        module
            .AddPage(CompositionIds.WorkspacePage, "Workspace", "/")
            .AddSections(CompositionIds.WorkspaceMainOutlet, isExtendable: true)
            .AddSection<OverviewSection>(
                CompositionIds.OverviewSection,
                "Overview",
                10);
    });

var registry = CompositionRegistry.FromModules(hostModule);
```

Blazor hosts can register modules independently in DI and then assemble the
composition services:

```csharp
builder.Services.AddCloudShellUiCompositionModule(
    CompositionModuleId.Host,
    module =>
    {
        module
            .AddPage(CompositionIds.WorkspacePage, "Workspace", "/")
            .AddSections(CompositionIds.WorkspaceMainOutlet, isExtendable: true)
            .AddSection<OverviewSection>(
                CompositionIds.OverviewSection,
                "Overview",
                10);
    });

builder.Services.AddCloudShellUiCompositionModule(
    CompositionModuleId.Create("sample-extension"),
    module =>
    {
        module
            .Extend(CompositionIds.WorkspaceMainSections)
            .AddSection<ExtensionContributionSection>(
                CompositionIds.ExtensionContributionSection,
                "Contributed section",
                20);
    });

builder.Services.AddCloudShellUiComposition();
```

This is still startup composition, not CloudShell extension discovery. It gives
hosts and packages a small integration point for contributing modules without
introducing a CMS/editor layer.

Multiple modules may contribute to the same menu ID. A host module can declare
the menu with `AddMenu(...)`, while another module can retrieve that menu target
with `GetMenu(...)` and add groups or items to it. During registry assembly,
menu registrations with the same `MenuId` are merged, groups with the same
`MenuGroupId` are merged, and menu item IDs must remain unique across all
contributions to that menu. Menu item projections still preserve the module
that contributed each item, so presenters and diagnostics can identify
ownership after the visible menu has been merged.

Cross-module extension depends on a shared composition host context. The module
that owns a page or section outlet publishes strongly typed extension points,
such as `CompositionSectionOutletExtensionPoint`, from that context. Every
module registration can receive the same host context and call
`Extend(extensionPoint)` with one of the published extension points. The
registry validates that the target outlet exists and is marked `IsExtendable`
when all modules are assembled.

`Extend(...)` should overload on typed extension-point handles, not on loose
ID values. A section outlet extension point returns a section-outlet builder
that can add sections. Future menu, slot, page, or command extension points
can add their own `Extend(...)` overloads that return builders appropriate for
those artifact kinds.

`Extend(PageId)` is available for page-level extension and returns a page
extension builder that can add extension-owned section outlets. The registry
only allows that when the target page is marked `IsExtendable`. `Extend(SectionId)`
is intentionally deferred until section-owned outlets and their parent page
context are first-class in the model; a section ID alone is not enough for a
separate module to safely add child content.

```csharp
builder.AddCompositionModule<ShellCompositionHostContext>(
    CompositionModuleId.Create("identity-settings"),
    (context, module) =>
    {
        module
            .Extend(context.Settings.MainSections)
            .AddSection<IdentitySettingsSection>(
                MySections.IdentitySettings,
                "Identity",
                10);
    });
```

CloudShell Hosting now registers the composition services during
`AddCloudShellUi()`. CloudShell extensions that reference Hosting and the
composition package can register modules through
`builder.AddCompositionModule(...)`; the UI-extension-host sample does this
for its sample workspace page and sidebar item. Plain Blazor hosts can still
use `builder.Services.AddCloudShellUiCompositionModule(...)` directly when
they are not inside the CloudShell extension builder. The legacy shell catalog
navigation bridge remains available for compatibility while extensions migrate
to composition-native menu contributions.

CloudShell Hosting now also has a shell-owned tabbed layout component that
matches the resource details information architecture: local navigation in a
left panel and selected content in a right panel. The common `/settings` page
uses this component and renders composition-backed settings sections. Its
public `ShellCompositionIds.SettingsPage` and
`ShellCompositionIds.SettingsMainOutlet` IDs are the initial CloudShell-owned
targets for future settings contributors. This is the first shell-owned
composition consumer. Resource Details and legacy custom shell views now use
the same layout through adapter code while keeping Resource Manager and
custom-view contribution models separate from the generic shell layout. The
standalone CompositionSandbox sample remains the place to explore layout
patterns before the shell adopts them.

The composition menu model represents named menu groups, menu items that can
live inside or outside a group, one level of menu sub-items through parent
item IDs, attributes, authorization metadata, artifact-ID targets, and direct
href targets. Icon data is stored as the namespaced
`CompositionAttributeNames.Icon` attribute instead of a first-class
`menuItem.Icon` property. Titles are also plain descriptor strings. A host can
render the title literally or interpret it as a localization key; the
composition engine does not localize titles itself. If a separate title
localization key becomes necessary later, prefer a namespaced
`TitleLocalizationKey` attribute. Avoid `TitleId`, because `Id` is already the
composition vocabulary for artifact identity and hierarchy. The current
CloudShell navigation renderer still owns active-route matching,
permission-driven visibility, localized labels, collapsed group state, and
Fluent-specific presentation. The migration should adapt those behaviors onto
composition menu projections before replacing the rendered `NavMenu`. The
plain `CompositionMenu` remains the standard non-framework-specific renderer;
a future CloudShell-hosted Fluent presenter should consume the same
composition projections while applying CloudShell navigation styling and
behavior.

CloudShell Hosting now includes a bridge that projects the legacy
`ShellCatalog.NavigationItems` into the `ShellCompositionIds.MainMenu`
composition menu. This bridge uses composition artifact targets for shell-owned
core pages that are already registered in the composition graph, and direct
href targets for legacy routes or external links that do not yet have
composition page IDs. The visible shell sidebar consumes that composition menu
through a CloudShell-owned Fluent presenter, preserving authorization
filtering, localization, active-route matching, collapsed submenus, and icon
interpretation through `CompositionAttributeNames.Icon`. New
composition-native menus should prefer artifact-ID targets when they point at
registered pages or sections, and href targets when they point outside the
composition graph.

Authorization is encoded in the composition graph as artifact metadata, not as
presentation logic. Pages, menus, menu groups, menu items, section outlets, and
sections can carry `CompositionAuthorizationRequirements` with CloudShell
permission names plus policy, role, and claim requirements for Blazor/ASP.NET
Core authorization adapters. The reusable composition library only stores and
projects these requirements. Presentation remains a renderer responsibility:
the current CloudShell Fluent sidebar evaluates the permission subset for
menus, groups, and items and hides unauthorized navigation, while future page
and section renderers can map the same metadata to `AuthorizeView`, access
denied states, disabled affordances, or development diagnostics.

Core shell navigation, Resource Manager, Observability, and the UI Extension
Host sample have moved their static sidebar items from legacy shell navigation
to composition-native menu contributions. Core shell contributes Overview,
Settings, Users, and Extensions directly under the shell-owned Workspace and
Platform menu groups. Resource Manager contributes Resources and Health
directly under the shared Workspace menu group and targets registered Resource
Manager page IDs. Resource Manager also registers Resource Details as a
canonical parameterized composition page with the route
`/resources/{resourceId}/{view?}`. Resource tab availability and rendering
remain Resource Manager concepts for now; the details page uses the
composition page target for canonical tab navigation while the existing route
helpers remain available as fallback and non-composition API. Resource Manager
UI code that has access to the composition registry should use the shared
Resource Manager composition-link helper when it needs a Resource Details URL.
Observability
contributes its Workspace parent item and child entries for Logs,
Dependencies, Service map, Traces, and Metrics with per-item permission
requirements in the graph. The UI Extension Host sample contributes its
sample workspace page and sidebar item through a composition module. The
legacy navigation bridge remains for unmigrated extension navigation items;
migrated items should be removed from legacy `AddNavigationItem(...)`
registration to avoid duplicate sidebar entries.

Future dashboard customization should use the same composition model. The
dashboard should expose section containers or slots that installed modules can
extend with custom content areas, while still letting the dashboard renderer
own layout. Extensions that need a full custom experience should be able to
register their own page and map a navigation item to it through composition
targets. That includes pages that embed a provider-owned tool such as Grafana;
the shell should route and authorize the page, but the embedded experience
remains extension-owned content rather than a built-in Metrics view.

The CloudShell main layout hosts `CompositionHost` in pass-through mode. Pages
that are registered in the composition graph receive a cascaded
`CompositionContext`; legacy routes that are not composition-native continue
to render normally while the shell migrates surfaces one at a time.

The standalone Composition Sandbox also includes a sample-owned Bootstrap
menu presenter. It reads the same namespaced icon attribute, interprets values
as Bootstrap Icons classes, and keeps that interpretation outside the generic
composition libraries.

Resource Manager now contributes its settings surface into the common
CloudShell `/settings` page through the settings section outlet. The original
`/resources/settings` route remains available for direct links and
compatibility, but the unified settings page is the composition-backed entry
point. Shell-owned settings sections should use composition targets when
linking to registered shell pages so route lookup stays behind the composition
registry during the migration. The Settings section outlet opts into child
addresses because `/settings/{section?}` treats selected settings sections as
stable child locations. The Settings page renderer still decides to present
those sections as tabs through a CloudShell-specific composition tabbed-layout
adapter so the shell can keep its Fluent/resource-details visual language
without forcing that presentation into the generic Blazor composition package.
The adapter preserves section module ownership on rendered tab items and
panels through `data-composition-module` attributes.

Resource Manager also registers its static shell pages as composition pages.
The composition link resolver can materialize route-template values, which
allows composition targets to resolve stable navigational locations such as
`/resources/{resourceId}` for the Resource Details container page and
`/resources/{resourceId}/{view}` for a selected Details tab/view, while leaving
non-navigational context in the query string. Resource Details accepts the
legacy `/resources/{resourceId}/details?tab=<group>:<view>` shape for
compatibility, but generated Resource Manager links use the path-shaped
convention. Conceptually, Resource Details is the parent page. It decides to
render its child sections as inline navigation, and its section outlet opts
into child addresses so the selected section can resolve as
`/resources/{resourceId}/{view}`. A normal page section remains
parent-addressed by default and is projected by the current URI resolver as a
page-local fragment.

Resource Details now consumes the same shell-owned tabbed layout component as
Settings while preserving the Resource Manager-owned tab contribution model,
generated fallback views, tab grouping, invalid-tab recovery, and
resource-scoped summary content. This keeps Resource Manager vocabulary behind
its adapter while moving the visual layout to the shared shell component.
Legacy custom shell views also consume the same layout and pass optional item
descriptions through the shared tab item model. The layout exposes neutral
`cloudshell-tabbed-*` CSS hooks; older `resource-tab-*` and registration
layout selectors remain for compatibility with Resource Manager-specific
surfaces that have not moved to the shared component.
When a tab item has a route or fragment target, the shared shell layout renders
it as a plain anchor with an `href`. This is a deliberate exception to the
Fluent-first shell rule because the tabbed layout needs direct row-level CSS
control for icons, descriptions, active state, and grouped local navigation
that the Fluent anchor wrapper does not expose cleanly. This keeps
route-backed local navigation linkable without making non-navigational tab
items pretend to have URLs.

`CompositionEngineHost` is an in-memory host for mounted modules. It owns the
currently mounted module list and rebuilds the active registry projection when
modules are mounted or unmounted:

```csharp
var host = new CompositionEngineHost([hostModule]);
host.Mount(extensionModule);
host.Unmount(extensionModule.Id);
```

This is still local in-memory composition. CloudShell extension discovery,
activation/deactivation rules, persistence, and artifact-level diagnostics are
future adapter work.

The registry also exposes basic page, menu, and section projection queries
that include the owning `CompositionModuleId`. Existing registration queries
remain available for simple renderers, while projection queries are the first
step toward diagnostics and renderer-specific views that need module
ownership. Menu item projections can be queried for a specific menu so
presenters do not need to scan registration lists when they need item-level
module metadata.

The Blazor renderers consume these projections where module ownership matters.
Menu, stacked section, and tabbed section renderers add
`data-composition-module` attributes to rendered navigation or section
elements so diagnostics and future tooling can trace visible content back to
the module that registered it. CloudShell's Fluent navigation presenter uses
the same menu item projection path for its root and child navigation items.

`isExtendable: true` marks a page or section outlet as an extension point.
Other modules can add section outlets only to pages that are marked
extendable, and can add sections only to outlets that are marked extendable.
This is a structural composition contract; authorization requirements decide
dynamically who may visit or see menus, pages, outlets, and sections that use
the graph. `CanBeReplaced` is a separate future policy: replacement would
allow another module to override an artifact or projection, and therefore needs
explicit conflict and ownership rules instead of being implied by
extensibility.

## Blazor Components

The Blazor library currently provides these components:

| Component | Purpose |
| --- | --- |
| `CompositionHost` | Resolves the current route to a registered page and cascades `CompositionContext` to child content. It can also receive an explicit `PageId`. Hosts can opt into pass-through rendering for routes that are not composition-registered yet. |
| `CompositionMenu` | Renders a registered menu, menu groups, root items, and sub-items. Menu items use composition targets rather than hard-coded routes. The current page or section target receives `aria-current="page"` and the active link class. Unmatched attributes attach to the rendered `<nav>` root, and class parameters customize the root/group/item/sub-item/link elements. |
| `CompositeAnchor` | Resolves a composition target into an anchor `href`. Page targets resolve to registered routes, section targets resolve to the nearest page route plus a fragment, menu item targets resolve through the menu item's own target, and href targets are emitted directly. When child content is omitted, the link uses the target artifact title where one exists. |
| `TitleOutlet` | Renders visible text for the current composition page title from the cascaded context, an explicit `Page`, or the current route. |
| `PageTitleOutlet` | Wraps Blazor `PageTitle` for the current composition page title from the cascaded context, an explicit `Page`, or the current route. Use this for the document title instead of mixing document-head behavior into visible page headers. |
| `CompositionPageLayout` | Renders a plain page frame with document title, visible title, optional eyebrow, summary, actions, navigation, and child content. Text-heavy regions are render fragments so hosts can localize or customize them. |
| `CompositionSectionContainer` | Cascades the current section outlet ID to nested content. |
| `CompositionSectionNavigation` | Renders normal anchor links to the registered sections in an outlet. Parent-addressed sections resolve to page-local hashes, while child-addressed sections resolve through the owning page route. |
| `CompositionSectionOutlet` | Renders all registered sections for the current page and section outlet using Blazor `DynamicComponent`. It can resolve the page from cascade, an explicit `Page`, or the current route. |
| `CompositionSectionTabs` | Renders registered named sections as tab items and uses composition section targets for tab links. It reads selection from child-address routes or parent-address fragments, and renders the selected section with `DynamicComponent`. It can resolve the page from cascade, an explicit `Page`, or the current route. |
| `CompositionTabbedPageLayout` | Composes `CompositionPageLayout`, `CompositionSectionContainer`, and `CompositionSectionTabs` into a reusable tabbed page layout. It is useful for settings-like pages while keeping tabs as a renderer choice over named sections. |

A typical layout hosts the composition root around the routed body:

```razor
@inherits LayoutComponentBase

<CompositionHost>
    <aside>
        <CompositionMenu Id="@CompositionIds.MainMenu" />
    </aside>

    <main>
        @Body
    </main>
</CompositionHost>
```

A routed page can then use the cascaded context:

```razor
@page "/reports"

<PageTitleOutlet />

<h1><TitleOutlet /></h1>

<CompositionSectionContainer Id="@CompositionIds.ReportsMainOutlet">
    <CompositionSectionNavigation />
    <CompositionSectionOutlet />
</CompositionSectionContainer>
```

For settings-like pages, a host can compose the same primitives through the
tabbed page layout:

```razor
<CompositionTabbedPageLayout Page="@CompositionIds.SettingsPage"
                             Outlet="@CompositionIds.SettingsMainOutlet"
                             TabsAriaLabel="Settings sections">
    <EyebrowContent>Settings</EyebrowContent>
    <SummaryContent>
        Configure this host through sections contributed to the settings page.
    </SummaryContent>
</CompositionTabbedPageLayout>
```

When a renderer is placed in a different render-mode island from the layout
that hosts `CompositionHost`, pass the page explicitly instead of relying only
on cascaded context:

```razor
<PageTitleOutlet Page="@CompositionIds.ReportsPage" />
<TitleOutlet Page="@CompositionIds.ReportsPage" />

<CompositionSectionContainer Id="@CompositionIds.ReportsMainOutlet">
    <CompositionSectionOutlet Page="@CompositionIds.ReportsPage" />
</CompositionSectionContainer>
```

The same named sections can be rendered as tabs when a page should show one
selected section at a time:

```razor
<CompositionSectionContainer Id="@CompositionIds.SettingsMainOutlet">
    <CompositionSectionTabs />
</CompositionSectionContainer>
```

The tabs component handles item routing through composition section targets and
renders the selected section. Visual classes are parameters so an app can use
Bootstrap, shell-specific classes, or plain CSS without changing the
composition model.

Routing remains normal Blazor routing. Razor components still declare routes
with `@page`; the composition registry records which composition page ID maps
to that route so menus, links, title outlets, and section outlets can resolve
the active page. The composition framework is convention-driven and
opinionated today: pages map to routable addresses, page sections belong to
their page address by default, and query strings carry local view state. The
future direction is a host-provided address projection pipeline rather than a
hard-coded URL convention. The convention is a contract between the resolver
and the UI that consumes the route: if composition resolves a target to
`/resources/{resourceId}/{view}`, the Resource Details component must declare
and interpret that same route shape.

Composition declares the logical structure. Composite IDs are the canonical
addresses: they uniquely identify artifacts and encode hierarchy without
requiring renderers to walk the tree. The registry can therefore keep
kind-specific maps from typed IDs to artifacts, projections, and relationships.
An address projector then turns a composition target into a rendered address
for a specific host. In Blazor that rendered address is usually a URL path,
fragment, or route value; another host could theoretically project the same
target into a different address form. The integrating UI framework still owns
route handling: in Blazor, ordinary Razor pages must declare and honor those
routes unless the host supplies a composition-aware router component that can
map composition route metadata to Blazor route entries.

Nested navigation is a presentation choice made by whatever renders the parent.
A page with two or more child pages, or a page with two or more sections, may
present that hierarchy as tabs, side navigation, cards, or no explicit nested
navigation. The first built-in address projection hints only tell the resolver
whether selected child content shares the parent rendered address or owns a
child address value. In the current Blazor route projection that can become
fragment-like navigation or path-like navigation, such as `/settings/platform`
or `/settings/resource-manager`, but those URI shapes are resolver details.
The current Settings renderer projects registered settings sections as
logical sub-pages in a nested navigation surface. Their full section IDs
remain the internal composition addresses, while the public child address
value is derived from the local identifier by convention.

Programmatic URL resolution follows the registered page route template. For
example, the Settings page is registered as `/settings/{section?}`. Resolving
`ShellCompositionIds.SettingsPage` with no route values yields `/settings`;
resolving the same page ID with `{ section = "platform" }` yields
`/settings/platform`. A renderer that projects sections as nested sub-pages
should resolve the owning page with route values rather than hand-building the
URL. A direct `SectionId` target remains a generic content address and
resolves to the owning page plus a fragment, such as
`/settings#section.cloudshell.settings.main.resource-manager`, unless a
host-provided projection maps that section into a route value.

This route mapping is convention-based for now, but the durable concept should
be address projection rather than the current low-level address-mode wording.
`Parent` and `Child` are useful first hints: parent-projected child content
shares the parent rendered address and can be addressed inside that parent
surface, while child-projected content owns a child address value within the
parent scope. A host-provided projector may materialize those hints as
fragments, path segments, route values, query values, or another address form.
The common case is one declaration on the logical group of sections, not route
metadata repeated on each individual section. Per-section metadata can remain
an override for unusual cases later. Address projection is orthogonal to
renderer choice: a consumer may render child-projected sections as tabs, side
navigation, cards, or no explicit nested navigation affordance at all. Even
then, the presentation layer must still be able to map the selected rendered
address back to the underlying `SectionId`, and the Blazor page must declare
routes that match the projection when the projection renders URLs.

A later host could go further and provide its own composition-aware Blazor
router component. That router would read page registrations, route templates,
and address-mode metadata from the composition registry, match incoming paths
to composition pages and selected child content, and render the registered
page component through Blazor component activation. That would reduce the
amount of duplicated route declaration in Razor pages and could eventually let
composition-rendered pages rely on composition route registrations instead of
`@page` directives.

That router is a routing integration layer over the graph, not the graph
itself. The graph should first be able to describe two page routing modes:

- **Composition-rendered pages**: the composition graph owns the route
  template and component metadata, and a composition-aware router can match
  and render the page.
- **Externally routed pages**: a normal Razor component still owns route
  matching through `@page`, but the route is registered against a composition
  page so menus, links, titles, section outlets, and nested navigation
  participate in the same graph.

Externally routed pages are important for product pages that need custom route
parameters or embedded local navigation. Resource Details is the practical
case: the Blazor page may own `/resources/{resourceId}` and
`/resources/{resourceId}/{view?}`, while the selected resource view is still
represented as composition child content under the Resource Details page. The
same pattern applies to settings pages, dashboards, or any page that renders
tabs or side navigation from registered child pages or sections.

Short-term model work should define the route primitives before building the
router: page route templates, page routing mode, selected-child route values,
section address mode, and enough component metadata for host-controlled
activation. The current implementation stays aligned with Blazor's built-in
router and uses the composition registry for link generation.

`PageTitleOutlet` relies on Blazor's normal `PageTitle` and `HeadOutlet`
behavior. Hosts still need to include a Blazor `HeadOutlet` in their app shell
if they want document titles to render during static SSR or interactive
rendering.

## Link Resolution

`CompositeAnchor` can target a page or any other resolvable artifact ID:

```razor
<CompositeAnchor Target="@CompositionIds.ReportsPage" />
```

`CompositeAnchor` uses the same registry resolver as menus and host-specific
presenters. It should be the default component when markup needs to link to a
composition artifact by ID instead of hard-coding the current route shape.
In the resolved case it renders a normal `<a>` element and passes unmatched
attributes to that anchor while keeping the resolved `href` authoritative.

It can also target a section:

```razor
<CompositeAnchor Target="@CompositionIds.ExtensionContributionSection">
    Open contributed section
</CompositeAnchor>
```

It can also target a menu item and inherit the menu item's title when child
content is omitted:

```razor
<CompositeAnchor Target="@CompositionIds.SettingsItem" />
```

If an artifact target or route template cannot resolve, `CompositeAnchor`
does not render a broken `href="#"` link. It renders a visible unresolved
placeholder by default, or the caller can provide an `Unresolved` template:

```razor
<CompositeAnchor Target="@CompositionIds.SettingsItem"
                 class="settings-link">
    <ChildContent>Settings</ChildContent>
    <Unresolved>
        <i>Failed to resolve link</i>
    </Unresolved>
</CompositeAnchor>
```

The core registry resolves:

- page target: `/reports`
- section target: `/#section.workspace.main.extension`

`CompositionSectionOutlet` renders each stacked section with an `id` equal to
the section ID. `CompositionSectionNavigation` uses the same target resolver,
so parent-addressed sections produce hash links that jump to those named
sections on the page.

Default Blazor composition components support unmatched attributes when there
is a clear HTML root to attach them to. `CompositeAnchor` splats onto the
resolved `<a>`, `CompositionMenu` splats onto its `<nav>`,
`CompositionPageLayout` splats onto its `<main>`, and
`CompositionSectionTabs` splats onto its tab container. Components that only
cascade context or conditionally render child content, such as
`CompositionHost` and `CompositionSectionContainer`, intentionally do not
capture arbitrary attributes because there is no stable rendered element to
own them. Use exposed class parameters or render-fragment templates when
customization needs to target nested markup.

Route parameters can be passed as query-string values through
`RouteParams`. When the registered route contains matching route-template
tokens, those values are used as escaped path segments and only the remaining
values become query parameters. For example, a registered route like
`/resources/{resourceId}/{view?}` can resolve route values into
`/resources/application%3Aapi` for the default Details view, or
`/resources/application%3Aapi/overview?traceId=...` for an explicit Details
tab/view plus query-scoped context.

The standard tab renderer uses section target resolution for its tab links.
When the outlet uses parent projection, those links resolve inside the page
address. When the outlet uses child projection, they resolve through the owning
page projection. Prefer child projection when the selected target is a stable
page, sub-page, or child-section location; keep query parameters for filters,
sort order, and other page-local state. A URL projector may still materialize
parent-projected sections as in-page focus targets or section anchors. Full
composition IDs remain stable internal addresses; a host or adapter may map
them to shorter, product-shaped URLs instead of exposing the entire
hierarchical ID in the path.

Public child address values are currently derived by convention from the
addressable artifact's local identifier in its parent scope. A host-provided
address projector should be able to override that convention from artifact
metadata or namespaced attributes when a product area needs a different public
shape. For example, Resource Manager keeps the typed view ID
`management:access-control` as the internal address, while its route value is
`access-control` under `/resources/{resourceId}`. The renderer still maps the
selected rendered address value back to the selected `SectionId`.

How sub-pages are rendered is up to the consumer. A page with child sub-pages
can be projected as tabs, side navigation, cards, or another local-navigation
component. Resource Details currently projects resource views as grouped tabs,
but the route convention treats those views as subordinate page locations under
the Resource Details page. The common Settings page follows the same pattern:
it projects settings entries as nested sub-pages under `/settings`, even though
the first implementation stores those entries as registered composition
sections.

## Sample

Run the standalone sample from the repository root:

```bash
dotnet run --project samples/CompositionSandbox --urls http://localhost:5102
```

Open:

```text
http://localhost:5102
```

The sample includes two registered pages:

- `/` renders the Workspace page and its registered sections.
- `/reports` renders a separate Reports page through the same menu, title
  outlet, and section outlet components.
- `/dashboard` renders the same registered section model through a
  sample-owned Bootstrap grid outlet. This demonstrates how host apps can
  explore layout patterns without adding a visual framework dependency to the
  base composition libraries.
- `/settings` and `/settings/{section}` render registered named sections
  through the reusable `CompositionTabbedPageLayout` component. The settings
  outlet opts into child addresses, so `/settings/advanced` can open the
  advanced section directly.

Registration titles are plain strings for this prototype and serve as the
stable fallback display value. Hosts may choose to treat the same value as a
localization key when rendering composition artifacts. The engine should not
own localization lookup. If we later need a separate key, use namespaced
attribute metadata such as a future `TitleLocalizationKey` instead of adding
localization-specific properties to every artifact. Avoid `TitleId`, because
composition IDs identify artifacts and hierarchy, not display resources.
Renderers that need richer display behavior can still expose templates or
localized title providers above the core graph.

## Current Validation

The core registry currently validates duplicate module, page, menu, and
section IDs when the graph is built. Tests cover route normalization, target
link resolution, section target links with route parameters, section ordering,
section metadata, menu registration, module assembly, in-memory module
mount/unmount, descriptor JSON round-trip, descriptor rehydration through a
component type resolver, module-owned projections, composed ID factories,
Blazor renderer projection consumption, duplicate ID validation, section
outlet registration, and extension-point validation.

Validation is intentionally still small. Future work should validate unknown
targets, missing parents, unsupported content kinds, module ownership
conflicts, permission metadata, and route conflicts.

## Not Implemented Yet

The current composition engine does not yet include:

- CoreShell adapter APIs that project shell-owned contribution
  contracts into composition modules
- Resource Manager adapters that project resource-specific contracts into
  composition modules
- persisted composition graph metadata
- `CompositionModuleBuilder` integration with a host or shell-owned
  activation/deactivation lifecycle
- CloudShell extension discovery and activation rules that decide which
  modules are mounted or unmounted by the shell adapter
- artifact-level module diagnostics beyond basic module ownership projections
- integration points for extensions, host modules, renderer outlets, metadata,
  visibility, and future persisted graph loading
- persisted descriptor storage
- richer runtime artifact instances and renderer-specific projections
- slots and sub-pages as first-class APIs
- permissions or visibility rules
- shell-specific metadata outlets beyond the plain `TitleOutlet` and
  `PageTitleOutlet`
- composition-aware Blazor router support; current pages still use normal
  Blazor routing and register route metadata for link generation and context
  resolution
- explicit localization metadata or title templates
- UI configuration or layout editing in the core package; CloudShell or
  another host can build its own CMS/editor experience on top of the
  composition infrastructure later, while the reusable layer stays focused on
  graph, descriptors, modules, integration points, and renderers

These are proposal-tracked directions, not current behavior.
