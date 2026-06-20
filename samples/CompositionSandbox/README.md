# Composition Sandbox

This is a clean Blazor application that uses the composition engine without
hosting CloudShell UI, Resource Manager, Control Plane services, or CloudShell
extension APIs.

The sample proves the first boundary:

- `CloudShell.UI.Composition` owns core facilities: typed IDs, registration,
  graph lookup, section ordering, and target-to-link resolution.
- `CloudShell.UI.Composition.Blazor` owns plain Blazor components that render
  registered menus, links, page titles, section containers, and section
  outlets.
- The app owns visual styling. This sample uses plain Bootstrap CSS plus a
  small app stylesheet; it does not use a Blazor Bootstrap component package.
- Page navigation uses normal Blazor routes. The menu and `CompositeAnchor`
  resolve registered page IDs to links, and `CompositionHost` resolves the
  active composition page from the current route.
- The reusable Blazor components are intended to be render-mode neutral:
  they render normal anchors and markup, use composition section targets for
  tab links, and can resolve page context from cascade, an explicit page ID,
  or the current route.
- Page headers use `TitleOutlet` for visible title text, while
  `PageTitleOutlet` wraps Blazor `PageTitle` so document titles flow
  through the standard `HeadOutlet` pipeline.
- The startup registration uses separate composition modules: the host module
  owns the shell pages and menu, and a sample extension module contributes a
  section to an extendable outlet.
- Layout patterns are explored in the sample app itself. The `/dashboard`
  route uses a sample-owned Bootstrap grid outlet over the same composition
  registry, while the other pages use the plain stacked section outlet from
  `CloudShell.UI.Composition.Blazor`.
- The `/settings/{section?}` route uses the reusable composition tab outlet
  from `CloudShell.UI.Composition.Blazor`, styles it with Bootstrap classes,
  and opts into child addresses so selected named sections resolve as
  `/settings/general` or `/settings/advanced`.

Future CloudShell extension integration should adapt extension contributions
into the core composition model after this standalone API shape is proven.
There is no current plan to build a composition editor UI for this sandbox;
CloudShell or another host can build a CMS or editor experience on top of the
composition infrastructure later. The sample is for programmatic graph and
renderer experiments.

## Run

From the repository root:

```bash
dotnet run --project samples/CompositionSandbox --urls http://localhost:5102
```

Open:

```text
http://localhost:5102
```

The sample also exposes:

```text
http://localhost:5102/reports
http://localhost:5102/dashboard
http://localhost:5102/settings
http://localhost:5102/settings/advanced
http://localhost:5102/#section.workspace.main.extension
```
