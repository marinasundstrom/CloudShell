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

The current implementation wraps string values. The proposed direction is
stronger composed ID value types, where child artifacts such as sections are
created from a local identifier plus the parent page, sub-page, slot, or
section outlet ID. That keeps hierarchy explicit while preserving a stable
serialized address.

The current graph supports:

- composition modules
- menus
- menu sections
- menu items
- pages bound to normal Blazor routes
- section outlets
- named sections rendered into a section outlet
- page and section targets resolved into links

Navigation hierarchy and content hierarchy are already separate. A menu item
targets a page or section by ID; it does not own that content.

Named sections are the current content contribution primitive. A section has a
stable `SectionId`, a display title, an order, and a component type. Renderers
can show those same sections as a stack, grid, tabs, or another layout pattern
without changing the registered content graph.

The current registry stores runtime registrations directly. The proposed
direction is to split artifact data into serializable descriptors, runtime
instances projected from descriptors, and renderer-ready projections. That
keeps future persistence possible without making component instances or
renderer state the durable data model.

## Registration

Host applications register the composition graph during startup:

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
        .AddSections(CompositionIds.WorkspaceMainOutlet, allowExtending: true)
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
        module.AddPage(CompositionIds.WorkspacePage, "Workspace", "/");
    });

var registry = CompositionRegistry.FromModules(hostModule);
```

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

`allowExtending: true` marks a section outlet as extendable. Only extendable
outlets can be reopened through `GetSections(...)`. This keeps extension-style
contribution explicit even before CloudShell extension adapters exist.

## Blazor Components

The Blazor library currently provides these components:

| Component | Purpose |
| --- | --- |
| `CompositionHost` | Resolves the current route to a registered page and cascades `CompositionContext` to child content. It can also receive an explicit `PageId`. |
| `CompositionMenu` | Renders a registered menu and menu sections. Menu items use composition targets rather than hard-coded routes. |
| `CompositionLink` | Resolves a page or section target into an anchor `href`. Page targets resolve to the registered route. Section targets resolve to the nearest page route plus a fragment. |
| `TitleOutlet` | Renders the title of the current composition page from the cascaded context. |
| `CompositionSectionContainer` | Cascades the current section outlet ID to nested content. |
| `CompositionSectionOutlet` | Renders all registered sections for the current page and section outlet using Blazor `DynamicComponent`. |
| `CompositionSectionTabs` | Renders registered named sections as tab items, stores the selected section in a query-string value, and renders the selected section with `DynamicComponent`. |

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

<h1><TitleOutlet /></h1>

<CompositionSectionContainer Id="@CompositionIds.ReportsMainOutlet">
    <CompositionSectionOutlet />
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

## Link Resolution

`CompositionLink` can target a page:

```razor
<CompositionLink Page="@CompositionIds.ReportsPage" />
```

It can also target a section:

```razor
<CompositionLink Target="@CompositionIds.ExtensionContributionSection">
    Open contributed section
</CompositionLink>
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
  `CompositionSectionTabs` component. The selected section is represented with
  a normal `section` query parameter.

Registration titles are plain strings for this prototype. Localization is
intentionally deferred. Likely options include localized title providers,
metadata localization keys, or title content templates on renderers that need
to opt into custom display behavior.

## Current Validation

The core registry currently validates duplicate module, page, menu, and
section IDs when the graph is built. Tests cover route normalization, target
link resolution, section target links with route parameters, section ordering,
section metadata, menu registration, module assembly, in-memory module
mount/unmount, duplicate ID validation, and extendable section outlet
validation.

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
- artifact-level module diagnostics in renderer projections
- serializable descriptor objects for all artifacts
- separate runtime artifact instances and renderer projections
- composed ID factories for parent/child artifact IDs
- slots and sub-pages as first-class APIs
- permissions or visibility rules
- shell-specific metadata outlets beyond the plain `TitleOutlet`
- active menu item selection
- localization metadata or title templates
- UI configuration or layout editing

These are proposal-tracked directions, not current behavior.
