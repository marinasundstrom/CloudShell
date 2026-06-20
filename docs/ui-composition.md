# UI Composition

CloudShell UI composition is an experimental layout/content engine for Blazor
applications. It is currently independent from CloudShell Hosting, Resource
Manager, and the CloudShell extension model. The first goal is to prove the
basic graph, routing bridge, and rendering components in a normal Blazor app
before adding CloudShell-specific adapters.

For future direction, see the
[shell composition proposal](proposals/core/shell-composition.md).

## Packages

The current implementation is split into two libraries:

| Library | Current role |
| --- | --- |
| `CloudShell.UI.Composition` | Core facilities: typed IDs, page/menu/section registrations, module building, registry builder, route lookup, section ordering, and target-to-link resolution. |
| `CloudShell.UI.Composition.Blazor` | Plain Blazor components and DI registration for rendering the core graph. It does not depend on Fluent UI, Bootstrap component packages, CloudShell Hosting, Resource Manager, or CloudShell extension contracts. |

The Blazor components render ordinary HTML elements and can be styled by the
host app with normal CSS. The `samples/CompositionSandbox` app demonstrates
this with plain Bootstrap CSS and a small app stylesheet.

Framework-specific presentation belongs in host adapters. The base Blazor
package should keep standard, render-mode-neutral components that emit normal
HTML and expose class parameters. CloudShell Hosting can add Fluent UI
presenters for the same composition projections, and another host can add
Bootstrap or custom presenters without changing the core graph.

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
builder.Services.AddCloudShellUiCompositionModule<ShellCompositionHostContext>(
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
`AddCloudShellUi()`. Existing CloudShell navigation and pages still render
through the current shell catalog. Extensions that reference the composition
package can register modules through `builder.Services.AddCloudShellUiCompositionModule(...)`;
the UI-extension-host sample does this for its sample workspace page. This is
the first integration seam, not a replacement renderer.

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
item IDs, attributes, permission metadata, artifact-ID targets, and direct
href targets. Icon data is stored as the namespaced
`CompositionAttributeNames.Icon` attribute instead of a first-class
`menuItem.Icon` property. The current
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
composition menu. This bridge deliberately uses direct href targets because
legacy navigation items are already resolved to routes or external links. The
visible shell sidebar consumes that composition menu through a CloudShell-owned
Fluent presenter, preserving authorization filtering, localization,
active-route matching, collapsed submenus, and icon interpretation through
`CompositionAttributeNames.Icon`. New composition-native menus should prefer
artifact-ID targets when they point at registered pages or sections, and href
targets when they point outside the composition graph.

The standalone Composition Sandbox also includes a sample-owned Bootstrap
menu presenter. It reads the same namespaced icon attribute, interprets values
as Bootstrap Icons classes, and keeps that interpretation outside the generic
composition libraries.

Resource Manager now contributes its settings surface into the common
CloudShell `/settings` page through the settings section outlet. The original
`/resources/settings` route remains available for direct links and
compatibility, but the unified settings page is the composition-backed entry
point.

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
This is a structural composition contract; future permissions and visibility
rules can decide dynamically who may register, mount, visit, or see modules
and sections that use the extension point. `CanBeReplaced` is a separate
future policy: replacement would allow another module to override an artifact
or projection, and therefore needs explicit conflict and ownership rules
instead of being implied by extensibility.

## Blazor Components

The Blazor library currently provides these components:

| Component | Purpose |
| --- | --- |
| `CompositionHost` | Resolves the current route to a registered page and cascades `CompositionContext` to child content. It can also receive an explicit `PageId`. |
| `CompositionMenu` | Renders a registered menu, menu groups, root items, and sub-items. Menu items use composition targets rather than hard-coded routes. |
| `CompositionLink` | Resolves a composition target into an anchor `href`. Page targets resolve to registered routes, section targets resolve to the nearest page route plus a fragment, menu item targets resolve through the menu item's own target, and href targets are emitted directly. When child content is omitted, the link uses the target artifact title where one exists. |
| `TitleOutlet` | Renders visible text for the current composition page title from the cascaded context, an explicit `Page`, or the current route. |
| `PageTitleOutlet` | Wraps Blazor `PageTitle` for the current composition page title from the cascaded context, an explicit `Page`, or the current route. Use this for the document title instead of mixing document-head behavior into visible page headers. |
| `CompositionPageLayout` | Renders a plain page frame with document title, visible title, optional eyebrow, summary, actions, navigation, and child content. Text-heavy regions are render fragments so hosts can localize or customize them. |
| `CompositionSectionContainer` | Cascades the current section outlet ID to nested content. |
| `CompositionSectionOutlet` | Renders all registered sections for the current page and section outlet using Blazor `DynamicComponent`. It can resolve the page from cascade, an explicit `Page`, or the current route. |
| `CompositionSectionTabs` | Renders registered named sections as tab items, stores the selected section in a query-string value, and renders the selected section with `DynamicComponent`. It can resolve the page from cascade, an explicit `Page`, or the current route. |
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
    <CompositionSectionOutlet />
</CompositionSectionContainer>
```

For settings-like pages, a host can compose the same primitives through the
tabbed page layout:

```razor
<CompositionTabbedPageLayout Page="@CompositionIds.SettingsPage"
                             Outlet="@CompositionIds.SettingsMainOutlet"
                             TabsAriaLabel="Settings sections"
                             QueryParameter="section">
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
    <CompositionSectionTabs QueryParameter="section" />
</CompositionSectionContainer>
```

The tabs component handles item routing and selected section rendering. Visual
classes are parameters so an app can use Bootstrap, shell-specific classes, or
plain CSS without changing the composition model.

Routing remains normal Blazor routing. Razor components still declare routes
with `@page`; the composition registry records which composition page ID maps
to that route so menus, links, title outlets, and section outlets can resolve
the active page.

`PageTitleOutlet` relies on Blazor's normal `PageTitle` and `HeadOutlet`
behavior. Hosts still need to include a Blazor `HeadOutlet` in their app shell
if they want document titles to render during static SSR or interactive
rendering.

## Link Resolution

`CompositionLink` can target a page or any other resolvable artifact ID:

```razor
<CompositionLink Target="@CompositionIds.ReportsPage" />
```

It can also target a section:

```razor
<CompositionLink Target="@CompositionIds.ExtensionContributionSection">
    Open contributed section
</CompositionLink>
```

It can also target a menu item and inherit the menu item's title when child
content is omitted:

```razor
<CompositionLink Target="@CompositionIds.SettingsItem" />
```

The core registry resolves:

- page target: `/reports`
- section target: `/#section.workspace.main.extension`

Route parameters can be passed as query-string values through
`RouteParams`.

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
- `/settings` renders registered named sections through the reusable
  `CompositionTabbedPageLayout` component. The selected section is represented
  with a normal `section` query parameter.

Registration titles are plain strings for this prototype. Localization is
intentionally deferred. Likely options include localized title providers,
metadata localization keys, or title content templates on renderers that need
to opt into custom display behavior.

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

- CloudShell extension adapter APIs
- Resource Manager adapters
- persisted composition graph metadata
- `CompositionModuleBuilder` integration with the CloudShell extension
  activation/deactivation lifecycle
- CloudShell extension discovery and activation rules for module mount/unmount
- artifact-level module diagnostics beyond basic module ownership projections
- integration points for extensions, host modules, renderer outlets, metadata,
  visibility, and future persisted graph loading
- persisted descriptor storage
- richer runtime artifact instances and renderer-specific projections
- slots and sub-pages as first-class APIs
- permissions or visibility rules
- shell-specific metadata outlets beyond the plain `TitleOutlet` and
  `PageTitleOutlet`
- active menu item selection
- localization metadata or title templates
- UI configuration or layout editing in the core package; CloudShell or
  another host can build its own CMS/editor experience on top of the
  composition infrastructure later, while the reusable layer stays focused on
  graph, descriptors, modules, integration points, and renderers

These are proposal-tracked directions, not current behavior.
